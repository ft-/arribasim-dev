/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using log4net;
using OpenMetaverse;
using System;
using System.IO;
using System.Reflection;

namespace OpenSim.Framework
{
    // The terrain is stored in the database as a blob with a 'revision' field.
    // Some implementations of terrain storage would fill the revision field with
    //    the time the terrain was stored. When real revisions were added and this
    //    feature removed, that left some old entries with the time in the revision
    //    field.
    // Thus, if revision is greater than 'RevisionHigh' then terrain db entry is
    //    left over and it is presumed to be 'Legacy256'.
    // Numbers are arbitrary and are chosen to to reduce possible mis-interpretation.
    // If a revision does not match any of these, it is assumed to be Legacy256.
    public enum DBTerrainRevision
    {
        // Terrain is 'double[256,256]'
        Legacy256 = 11,
        // Terrain is 'int32, int32, float[,]' where the ints are X and Y dimensions
        // The dimensions are presumed to be multiples of 16 and, more likely, multiples of 256.
        Variable2D = 22,
        // Terrain is 'int32, int32, int32, int16[]' where the ints are X and Y dimensions
        //   and third int is the 'compression factor'. The heights are compressed as
        //   "int compressedHeight = (int)(height * compressionFactor);"
        // The dimensions are presumed to be multiples of 16 and, more likely, multiples of 256.
        Compressed2D = 27,
        // A revision that is not listed above or any revision greater than this value is 'Legacy256'.
        RevisionHigh = 1234
    }

    // Version of terrain that is a heightmap.
    // This should really be 'LLOptimizedHeightmapTerrainData' as it includes knowledge
    //    of 'patches' which are 16x16 terrain areas which can be sent separately to the viewer.
    // The heighmap is kept as an array of integers. The integer values are converted to
    //    and from floats by TerrainCompressionFactor.
    public class HeightMapTerrainData
    {
        public int SizeX { get; protected set; }
        public int SizeY { get; protected set; }
        public int SizeZ { get; protected set; }

        // A height used when the user doesn't specify anything
        public const float DefaultTerrainHeight = 21f;

        // Given a revision code and a blob from the database, create and return the right type of TerrainData.
        // The sizes passed are the expected size of the region. The database info will be used to
        //     initialize the heightmap of that sized region with as much data is in the blob.
        // Return created TerrainData or 'null' if unsuccessful.
        public static HeightMapTerrainData CreateFromDatabaseBlobFactory(int pSizeX, int pSizeY, int pSizeZ, int pFormatCode, byte[] pBlob)
        {
            // For the moment, there is only one implementation class
            return new HeightMapTerrainData(pSizeX, pSizeY, pSizeZ, pFormatCode, pBlob);
        }

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string LogHeader = "[HEIGHTMAP TERRAIN DATA]";

        public uint[,] m_TerrainPatchSerial;
        struct TerrainPatchInfo
        {
            public uint Serial; /* 0 == invalid, m_TerrainPatchSerial never holds any zeroes */
            public byte[] CompressedPatch;
            public int BitLength;
        }
        TerrainPatchInfo[,] m_PackedTerrainPatches;
        object m_PackedSerialLock = new object();

        // TerrainData.this[x, y]
        public float this[int x, int y]
        {
            get { return FromCompressedHeight(m_heightmap[x, y]); }
            set {
                int newVal = ToCompressedHeight(value);
                lock (m_PackedSerialLock)
                {
                    if (m_heightmap[x, y] != newVal)
                    {
                        m_heightmap[x, y] = newVal;
                        m_taint[x / Constants.TerrainPatchSize, y / Constants.TerrainPatchSize] = true;
                        if(++m_TerrainPatchSerial[x / Constants.TerrainPatchSize, y / Constants.TerrainPatchSize] == 0)
                        {
                            m_TerrainPatchSerial[x / Constants.TerrainPatchSize, y / Constants.TerrainPatchSize] = 1;
                        }
                    }
                }
            }
        }

        // TerrainData.this[x, y, z]
        public float this[int x, int y, int z]
        {
            get { return this[x, y]; }
            set { this[x, y] = value; }
        }

        // TerrainData.ClearTaint
        public void ClearTaint()
        {
            SetAllTaint(false);
        }

        // TerrainData.TaintAllTerrain
        public void TaintAllTerrain()
        {
            SetAllTaint(true);
        }

        private void SetAllTaint(bool setting)
        {
            for (int ii = 0; ii < m_taint.GetLength(0); ii++)
                for (int jj = 0; jj < m_taint.GetLength(1); jj++)
                    m_taint[ii, jj] = setting;
        }

        // TerrainData.ClearLand
        public void ClearLand()
        {
            ClearLand(DefaultTerrainHeight);
        }
        // TerrainData.ClearLand(float)
        public void ClearLand(float pHeight)
        {
            int flatHeight = ToCompressedHeight(pHeight);
            for (int xx = 0; xx < SizeX; xx++)
                for (int yy = 0; yy < SizeY; yy++)
                    lock (m_PackedSerialLock)
                    {
                        m_heightmap[xx, yy] = flatHeight;
                        if (++m_TerrainPatchSerial[xx / Constants.TerrainPatchSize, yy / Constants.TerrainPatchSize] == 0)
                        {
                            m_TerrainPatchSerial[xx / Constants.TerrainPatchSize, yy / Constants.TerrainPatchSize] = 1;
                        }
                    }
        }

        // Return 'true' of the patch that contains these region coordinates has been modified.
        // Note that checking the taint clears it.
        // There is existing code that relies on this feature.
        public bool IsTaintedAt(int xx, int yy, bool clearOnTest)
        {
            int tx = xx / Constants.TerrainPatchSize;
            int ty = yy / Constants.TerrainPatchSize;
            bool ret =  m_taint[tx, ty];
            if (ret && clearOnTest)
                m_taint[tx, ty] = false;
            return ret;
        }

        // Old form that clears the taint flag when we check it.
        public bool IsTaintedAt(int xx, int yy)
        {
            return IsTaintedAt(xx, yy, true /* clearOnTest */);
        }

        // TerrainData.GetDatabaseBlob
        // The user wants something to store in the database.
        public bool GetDatabaseBlob(out int DBRevisionCode, out Array blob)
        {
            bool ret = false;
            if (SizeX == Constants.RegionSize && SizeY == Constants.RegionSize)
            {
                DBRevisionCode = (int)DBTerrainRevision.Legacy256;
                blob = ToLegacyTerrainSerialization();
                ret = true;
            }
            else
            {
                DBRevisionCode = (int)DBTerrainRevision.Compressed2D;
                blob = ToCompressedTerrainSerialization();
                ret = true;
            }
            return ret;
        }

        // TerrainData.CompressionFactor
        private float m_compressionFactor = 100.0f;
        public float CompressionFactor { get { return m_compressionFactor; } }

        // TerrainData.GetCompressedMap
        public int[] GetCompressedMap()
        {
            int[] newMap = new int[SizeX * SizeY];

            int ind = 0;
            for (int xx = 0; xx < SizeX; xx++)
                for (int yy = 0; yy < SizeY; yy++)
                    newMap[ind++] = m_heightmap[xx, yy];

            return newMap;

        }
        // TerrainData.Clone
        public HeightMapTerrainData Clone()
        {
            HeightMapTerrainData ret = new HeightMapTerrainData(SizeX, SizeY, SizeZ);
            ret.m_heightmap = (int[,])this.m_heightmap.Clone();
            return ret;
        }

        // TerrainData.GetFloatsSerialized
        // This one dimensional version is ordered so height = map[y*sizeX+x];
        // DEPRECATED: don't use this function as it does not retain the dimensions of the terrain
        //     and the caller will probably do the wrong thing if the terrain is not the legacy 256x256.
        public float[] GetFloatsSerialized()
        {
            int points = SizeX * SizeY;
            float[] heights = new float[points];

            int idx = 0;
            for (int jj = 0; jj < SizeY; jj++)
                for (int ii = 0; ii < SizeX; ii++)
                {
                    heights[idx++] = FromCompressedHeight(m_heightmap[ii, jj]);
                }

            return heights;
        }

        // TerrainData.GetDoubles
        public double[,] GetDoubles()
        {
            double[,] ret = new double[SizeX, SizeY];
            for (int xx = 0; xx < SizeX; xx++)
                for (int yy = 0; yy < SizeY; yy++)
                    ret[xx, yy] = FromCompressedHeight(m_heightmap[xx, yy]);

            return ret;
        }


        // =============================================================

        private int[,] m_heightmap;
        // Remember subregions of the heightmap that has changed.
        private bool[,] m_taint;

        // To save space (especially for large regions), keep the height as a short integer
        //    that is coded as the float height times the compression factor (usually '100'
        //    to make for two decimal points).
        public int ToCompressedHeight(double pHeight)
        {
            return (int)(pHeight * CompressionFactor);
        }

        public float FromCompressedHeight(int pHeight)
        {
            return ((float)pHeight) / CompressionFactor;
        }

        void InitTerrainPatchCompressor()
        {
            /* tracking a serial allows a far more optimized handling of layer data transmission */
            m_TerrainPatchSerial = new uint[SizeX / Constants.TerrainPatchSize, SizeY / Constants.TerrainPatchSize];
            for (uint px = 0; px < SizeX / Constants.TerrainPatchSize; ++px)
            {
                for (uint py = 0; py < SizeY / Constants.TerrainPatchSize; ++py)
                {
                    m_TerrainPatchSerial[px, py] = 1;
                }
            }
            m_PackedTerrainPatches = new TerrainPatchInfo[SizeX / Constants.TerrainPatchSize, SizeY / Constants.TerrainPatchSize];
        }

        // To keep with the legacy theme, create an instance of this class based on the
        //     way terrain used to be passed around.
        public HeightMapTerrainData(double[,] pTerrain)
        {
            SizeX = pTerrain.GetLength(0);
            SizeY = pTerrain.GetLength(1);
            SizeZ = (int)Constants.RegionHeight;
            m_compressionFactor = 100.0f;

            m_heightmap = new int[SizeX, SizeY];
            for (int ii = 0; ii < SizeX; ii++)
            {
                for (int jj = 0; jj < SizeY; jj++)
                {
                    m_heightmap[ii, jj] = ToCompressedHeight(pTerrain[ii, jj]);

                }
            }

            InitTerrainPatchCompressor();

            // m_log.DebugFormat("{0} new by doubles. sizeX={1}, sizeY={2}, sizeZ={3}", LogHeader, SizeX, SizeY, SizeZ);

            m_taint = new bool[SizeX / Constants.TerrainPatchSize, SizeY / Constants.TerrainPatchSize];
            ClearTaint();
        }

        // Create underlying structures but don't initialize the heightmap assuming the caller will immediately do that
        public HeightMapTerrainData(int pX, int pY, int pZ)
        {
            SizeX = pX;
            SizeY = pY;
            SizeZ = pZ;
            m_compressionFactor = 100.0f;
            m_heightmap = new int[SizeX, SizeY];
            m_taint = new bool[SizeX / Constants.TerrainPatchSize, SizeY / Constants.TerrainPatchSize];

            InitTerrainPatchCompressor();

            // m_log.DebugFormat("{0} new by dimensions. sizeX={1}, sizeY={2}, sizeZ={3}", LogHeader, SizeX, SizeY, SizeZ);
            ClearTaint();
            ClearLand(0f);
        }

        public HeightMapTerrainData(int[] cmap, float pCompressionFactor, int pX, int pY, int pZ) 
            : this(pX, pY, pZ)
        {
            m_compressionFactor = pCompressionFactor;
            int ind = 0;
            for (int xx = 0; xx < SizeX; xx++)
                for (int yy = 0; yy < SizeY; yy++)
                    m_heightmap[xx, yy] = cmap[ind++];
            // m_log.DebugFormat("{0} new by compressed map. sizeX={1}, sizeY={2}, sizeZ={3}", LogHeader, SizeX, SizeY, SizeZ);
        }

        // Create a heighmap from a database blob
        public HeightMapTerrainData(int pSizeX, int pSizeY, int pSizeZ, int pFormatCode, byte[] pBlob)
            : this(pSizeX, pSizeY, pSizeZ)
        {
            switch ((DBTerrainRevision)pFormatCode)
            {
                case DBTerrainRevision.Compressed2D:
                    FromCompressedTerrainSerialization(pBlob);
                    m_log.DebugFormat("{0} HeightmapTerrainData create from Compressed2D serialization. Size=<{1},{2}>", LogHeader, SizeX, SizeY);
                    break;
                default:
                    FromLegacyTerrainSerialization(pBlob);
                    m_log.DebugFormat("{0} HeightmapTerrainData create from legacy serialization. Size=<{1},{2}>", LogHeader, SizeX, SizeY);
                    break;
            }
        }

        // Just create an array of doubles. Presumes the caller implicitly knows the size.
        public Array ToLegacyTerrainSerialization()
        {
            Array ret = null;

            using (MemoryStream str = new MemoryStream((int)Constants.RegionSize * (int)Constants.RegionSize * sizeof(double)))
            {
                using (BinaryWriter bw = new BinaryWriter(str))
                {
                    for (int xx = 0; xx < Constants.RegionSize; xx++)
                    {
                        for (int yy = 0; yy < Constants.RegionSize; yy++)
                        {
                            double height = this[xx, yy];
                            if (height == 0.0)
                                height = double.Epsilon;
                            bw.Write(height);
                        }
                    }
                }
                ret = str.ToArray();
            }
            return ret;
        }

        // Just create an array of doubles. Presumes the caller implicitly knows the size.
        public void FromLegacyTerrainSerialization(byte[] pBlob)
        {
            // In case database info doesn't match real terrain size, initialize the whole terrain.
            ClearLand();

            using (MemoryStream mstr = new MemoryStream(pBlob))
            {
                using (BinaryReader br = new BinaryReader(mstr))
                {
                    for (int xx = 0; xx < (int)Constants.RegionSize; xx++)
                    {
                        for (int yy = 0; yy < (int)Constants.RegionSize; yy++)
                        {
                            float val = (float)br.ReadDouble();
                            if (xx < SizeX && yy < SizeY)
                            {
                                lock (m_PackedSerialLock)
                                {
                                    m_heightmap[xx, yy] = ToCompressedHeight(val);
                                    if (++m_TerrainPatchSerial[xx / Constants.TerrainPatchSize, yy / Constants.TerrainPatchSize] == 0)
                                    {
                                        m_TerrainPatchSerial[xx / Constants.TerrainPatchSize, yy / Constants.TerrainPatchSize] = 1;
                                    }
                                }
                            }
                        }
                    }
                }
                ClearTaint();
            }
        }
        
        // See the reader below.
        public Array ToCompressedTerrainSerialization()
        {
            Array ret = null;
            using (MemoryStream str = new MemoryStream((3 * sizeof(Int32)) + (SizeX * SizeY * sizeof(Int16))))
            {
                using (BinaryWriter bw = new BinaryWriter(str))
                {
                    bw.Write((Int32)DBTerrainRevision.Compressed2D);
                    bw.Write((Int32)SizeX);
                    bw.Write((Int32)SizeY);
                    bw.Write((Int32)CompressionFactor);
                    for (int yy = 0; yy < SizeY; yy++)
                        for (int xx = 0; xx < SizeX; xx++)
                        {
                            bw.Write((Int16)m_heightmap[xx, yy]);
                        }
                }
                ret = str.ToArray();
            }
            return ret;
        }

        // Initialize heightmap from blob consisting of:
        //    int32, int32, int32, int32, int16[]
        //    where the first int32 is format code, next two int32s are the X and y of heightmap data and
        //    the forth int is the compression factor for the following int16s
        // This is just sets heightmap info. The actual size of the region was set on this instance's
        //    creation and any heights not initialized by theis blob are set to the default height.
        public void FromCompressedTerrainSerialization(byte[] pBlob)
        {
            Int32 hmFormatCode, hmSizeX, hmSizeY, hmCompressionFactor;

            using (MemoryStream mstr = new MemoryStream(pBlob))
            {
                using (BinaryReader br = new BinaryReader(mstr))
                {
                    hmFormatCode = br.ReadInt32();
                    hmSizeX = br.ReadInt32();
                    hmSizeY = br.ReadInt32();
                    hmCompressionFactor = br.ReadInt32();

                    m_compressionFactor = hmCompressionFactor;

                    // In case database info doesn't match real terrain size, initialize the whole terrain.
                    ClearLand();

                    for (int yy = 0; yy < hmSizeY; yy++)
                    {
                        for (int xx = 0; xx < hmSizeX; xx++)
                        {
                            Int16 val = br.ReadInt16();
                            if (xx < SizeX && yy < SizeY)
                            {
                                lock(m_PackedSerialLock)
                                {
                                    m_heightmap[xx, yy] = val;
                                    if(++m_TerrainPatchSerial[xx / Constants.TerrainPatchSize, yy / Constants.TerrainPatchSize] == 0)
                                    {
                                        m_TerrainPatchSerial[xx / Constants.TerrainPatchSize, yy / Constants.TerrainPatchSize] = 1;
                                    }
                                }
                            }
                        }
                    }
                }
                ClearTaint();

                m_log.InfoFormat("{0} Read compressed 2d heightmap. Heightmap size=<{1},{2}>. Region size=<{3},{4}>. CompFact={5}",
                                LogHeader, hmSizeX, hmSizeY, SizeX, SizeY, hmCompressionFactor);
            }
        }

        public byte[] GetCompressedPatch(int patchx, int patchy, out int bitlength, uint lastSerialNo, out uint serialNo)
        {
            lock(m_PackedSerialLock)
            {
                if(m_PackedTerrainPatches[patchx, patchy].Serial == lastSerialNo && 0 != lastSerialNo)
                {
                    /* signal no change via null */
                    serialNo = lastSerialNo;
                    bitlength = 0;
                    return null;
                }

                if(m_PackedTerrainPatches[patchx, patchy].Serial != m_TerrainPatchSerial[patchx, patchy])
                {

                    m_PackedTerrainPatches[patchx, patchy].Serial = m_TerrainPatchSerial[patchx, patchy];
                    m_PackedTerrainPatches[patchx, patchy].CompressedPatch = new byte[651]; /* maximum length of a single compressed patch */
                    BitPack bitpack = new BitPack(m_PackedTerrainPatches[patchx, patchy].CompressedPatch, 0);
                    OpenSimTerrainCompressor.CreatePatchFromHeightmap(bitpack, this, patchx, patchy);
                    m_PackedTerrainPatches[patchx, patchy].BitLength = 8 * bitpack.BytePos + bitpack.BitPos;
                    if(0 == bitpack.BitPos)
                    {
                        m_PackedTerrainPatches[patchx, patchy].BitLength += 8;
                    }
                }

                bitlength = m_PackedTerrainPatches[patchx, patchy].BitLength;
                byte[] copy = new byte[m_PackedTerrainPatches[patchx, patchy].CompressedPatch.Length];
                Buffer.BlockCopy(m_PackedTerrainPatches[patchx, patchy].CompressedPatch, 0, copy, 0, copy.Length);
                serialNo = m_PackedTerrainPatches[patchx, patchy].Serial;

                return copy;
            }

        }
    }
}

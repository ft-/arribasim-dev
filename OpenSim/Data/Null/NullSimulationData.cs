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

using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using System.Collections.Generic;

namespace OpenSim.Data.Null
{
    /// <summary>
    /// NULL DataStore, do not store anything
    /// </summary>
    public class NullSimulationData : ISimulationDataStore
    {
        public NullSimulationData()
        {
        }

        public NullSimulationData(string connectionString)
        {
            Initialise(connectionString);
        }

        public void Initialise(string dbfile)
        {
            return;
        }

        public void Dispose()
        {
        }

        public void StoreRegionSettings(RegionSettings rs)
        {
        }

        public RegionLightShareData LoadRegionWindlightSettings(UUID regionUUID)
        {
            //This connector doesn't support the windlight module yet
            //Return default LL windlight settings
            return new RegionLightShareData();
        }

        public void RemoveRegionWindlightSettings(UUID regionID)
        {
        }

        public void StoreRegionWindlightSettings(RegionLightShareData wl)
        {
            //This connector doesn't support the windlight module yet
        }

        #region Environment Settings

        private ThreadedClasses.RwLockedDictionary<UUID, string> EnvironmentSettings = new ThreadedClasses.RwLockedDictionary<UUID, string>();

        public string LoadRegionEnvironmentSettings(UUID regionUUID)
        {
            string val;
            if (EnvironmentSettings.TryGetValue(regionUUID, out val))
            {
                return val;
            }
            return string.Empty;
        }

        public void StoreRegionEnvironmentSettings(UUID regionUUID, string settings)
        {
            EnvironmentSettings[regionUUID] = settings;
        }

        public void RemoveRegionEnvironmentSettings(UUID regionUUID)
        {
            EnvironmentSettings.Remove(regionUUID);
        }
        #endregion

        public RegionSettings LoadRegionSettings(UUID regionUUID)
        {
            RegionSettings rs = new RegionSettings();
            rs.RegionUUID = regionUUID;
            return rs;
        }

        public void StoreObject(SceneObjectGroup obj, UUID regionUUID)
        {
        }

        public void RemoveObject(UUID obj, UUID regionUUID)
        {
        }

        public void StorePrimInventory(UUID primID, ICollection<TaskInventoryItem> items)
        {
        }

        public List<SceneObjectGroup> LoadObjects(UUID regionUUID)
        {
            return new List<SceneObjectGroup>();
        }

        ThreadedClasses.RwLockedDictionary<UUID, HeightMapTerrainData> m_terrains = new ThreadedClasses.RwLockedDictionary<UUID, HeightMapTerrainData>();
        public void StoreTerrain(HeightMapTerrainData ter, UUID regionID)
        {
            m_terrains[regionID] = ter;
        }

        // Legacy. Just don't do this.
        public void StoreTerrain(double[,] ter, UUID regionID)
        {
            HeightMapTerrainData terrData = new HeightMapTerrainData(ter);
            StoreTerrain(terrData, regionID);
        }

        // Legacy. Just don't do this.
        // Returns 'null' if region not found
        public double[,] LoadTerrain(UUID regionID)
        {
            HeightMapTerrainData data;
            if (m_terrains.TryGetValue(regionID, out data))
            {
                return data.GetDoubles();
            }
            return null;
        }

        public HeightMapTerrainData LoadTerrain(UUID regionID, int pSizeX, int pSizeY, int pSizeZ)
        {
            HeightMapTerrainData val;
            if (m_terrains.TryGetValue(regionID, out val))
            {
                return val;
            }
            return null;
        }

        public void RemoveLandObject(UUID globalID)
        {
        }

        public void StoreLandObject(ILandObject land)
        {
        }

        public List<LandData> LoadLandObjects(UUID regionUUID)
        {
            return new List<LandData>();
        }

        public void Shutdown()
        {
        }

        public void SaveExtra(UUID regionID, string name, string value)
        {
        }

        public void RemoveExtra(UUID regionID, string name)
        {
        }

        public Dictionary<string, string> GetExtra(UUID regionID)
        {
            return null;
        }
    }
}

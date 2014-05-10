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

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Framework
{
    /// <summary>
    /// This class stores and retrieves dynamic objects.
    /// </summary>
    /// <remarks>
    /// Experimental - DO NOT USE.  Does not yet have namespace support.
    /// </remarks>
    public class DOMap
    {
        private IDictionary<string, object> m_map;
        private ReaderWriterLock m_mapRwLock = new ReaderWriterLock();
        
        public void Add(string ns, string objName, object dynObj)
        {
            DAMap.ValidateNamespace(ns);

            m_mapRwLock.AcquireWriterLock(-1);
            try
            {
                if (m_map == null)
                    m_map = new Dictionary<string, object>();

                m_map.Add(objName, dynObj);
            }
            finally
            {
                m_mapRwLock.ReleaseWriterLock();
            }
        }

        public bool ContainsKey(string key)
        {
            m_mapRwLock.AcquireReaderLock(-1);
            try
            {
                return Get(key) != null;
            }
            finally
            {
                m_mapRwLock.ReleaseReaderLock();
            }
        }

        /// <summary>
        /// Get a dynamic object
        /// </summary>
        /// <remarks>
        /// Not providing an index method so that users can't casually overwrite each other's objects.
        /// </remarks>
        /// <param name='key'></param>
        public object Get(string key)
        {
            m_mapRwLock.AcquireReaderLock(-1);
            try
            {
                if (m_map == null)
                    return null;
                else
                    return m_map[key];
            }
            finally
            {
                m_mapRwLock.ReleaseReaderLock();
            }
        }

        public bool Remove(string key)
        {
            m_mapRwLock.AcquireWriterLock(-1);
            try
            {
                if (m_map == null)
                    return false;
                else
                    return m_map.Remove(key);
            }
            finally
            {
                m_mapRwLock.ReleaseWriterLock();
            }
        }
    }
}
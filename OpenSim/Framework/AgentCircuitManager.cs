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

using System.Collections.Generic;
using OpenMetaverse;
using System.Threading;

namespace OpenSim.Framework
{
    /// <summary>
    /// Manage client circuits
    /// </summary>
    public class AgentCircuitManager
    {
        /// <summary>
        /// Agent circuits indexed by circuit code.
        /// </summary>
        /// <remarks>
        /// We lock this for operations both on this dictionary and on m_agentCircuitsByUUID
        /// </remarks>
        private Dictionary<uint, AgentCircuitData> m_agentCircuits = new Dictionary<uint, AgentCircuitData>();
        private ReaderWriterLock m_AgentCircuitsRwLock = new ReaderWriterLock();

        /// <summary>
        /// Agent circuits indexed by agent UUID.
        /// </summary>
        private Dictionary<UUID, AgentCircuitData> m_agentCircuitsByUUID = new Dictionary<UUID, AgentCircuitData>();

        public virtual AuthenticateResponse AuthenticateSession(UUID sessionID, UUID agentID, uint circuitcode)
        {
            AgentCircuitData validcircuit = null;

            m_AgentCircuitsRwLock.AcquireReaderLock(-1);
            try
            {
                if (m_agentCircuits.ContainsKey(circuitcode))
                    validcircuit = m_agentCircuits[circuitcode];
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseReaderLock();
            }

            AuthenticateResponse user = new AuthenticateResponse();

            if (validcircuit == null)
            {
                //don't have this circuit code in our list
                user.Authorised = false;
                return user;
            }

            if ((sessionID == validcircuit.SessionID) && (agentID == validcircuit.AgentID))
            {
                user.Authorised = true;
                user.LoginInfo = new Login();
                user.LoginInfo.Agent = agentID;
                user.LoginInfo.Session = sessionID;
                user.LoginInfo.SecureSession = validcircuit.SecureSessionID;
                user.LoginInfo.First = validcircuit.firstname;
                user.LoginInfo.Last = validcircuit.lastname;
                user.LoginInfo.InventoryFolder = validcircuit.InventoryFolder;
                user.LoginInfo.BaseFolder = validcircuit.BaseFolder;
                user.LoginInfo.StartPos = validcircuit.startpos;
            }
            else
            {
                // Invalid
                user.Authorised = false;
            }

            return user;
        }

        /// <summary>
        /// Add information about a new circuit so that later on we can authenticate a new client session.
        /// </summary>
        /// <param name="circuitCode"></param>
        /// <param name="agentData"></param>
        public virtual void AddNewCircuit(uint circuitCode, AgentCircuitData agentData)
        {
            m_AgentCircuitsRwLock.AcquireWriterLock(-1);
            try
            {
                if (m_agentCircuits.ContainsKey(circuitCode))
                {
                    m_agentCircuits[circuitCode] = agentData;
                    m_agentCircuitsByUUID[agentData.AgentID] = agentData;
                }
                else
                {
                    m_agentCircuits.Add(circuitCode, agentData);
                    m_agentCircuitsByUUID[agentData.AgentID] = agentData;
                }
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseWriterLock();
            }
        }

        public virtual void RemoveCircuit(uint circuitCode)
        {
            m_AgentCircuitsRwLock.AcquireWriterLock(-1);
            try
            {
                if (m_agentCircuits.ContainsKey(circuitCode))
                {
                    UUID agentID = m_agentCircuits[circuitCode].AgentID;
                    m_agentCircuits.Remove(circuitCode);
                    m_agentCircuitsByUUID.Remove(agentID);
                }
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseWriterLock();
            }
        }

        public virtual void RemoveCircuit(UUID agentID)
        {
            m_AgentCircuitsRwLock.AcquireWriterLock(-1);
            try
            {
                if (m_agentCircuitsByUUID.ContainsKey(agentID))
                {
                    uint circuitCode = m_agentCircuitsByUUID[agentID].circuitcode;
                    m_agentCircuits.Remove(circuitCode);
                    m_agentCircuitsByUUID.Remove(agentID);
                }
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseWriterLock();
            }
        }

        public AgentCircuitData GetAgentCircuitData(uint circuitCode)
        {
            AgentCircuitData agentCircuit = null;

            m_AgentCircuitsRwLock.AcquireReaderLock(-1);
            try
            {
                m_agentCircuits.TryGetValue(circuitCode, out agentCircuit);

                return agentCircuit;
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseReaderLock();
            }
        }

        public AgentCircuitData GetAgentCircuitData(UUID agentID)
        {
            AgentCircuitData agentCircuit = null;

            m_AgentCircuitsRwLock.AcquireReaderLock(-1);
            try
            {
                m_agentCircuitsByUUID.TryGetValue(agentID, out agentCircuit);
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseReaderLock();
            }

            return agentCircuit;
        }

        /// <summary>
        /// Get all current agent circuits indexed by agent UUID.
        /// </summary>
        /// <returns></returns>
        public Dictionary<UUID, AgentCircuitData> GetAgentCircuits()
        {
            m_AgentCircuitsRwLock.AcquireReaderLock(-1);
            try
            {
                return new Dictionary<UUID, AgentCircuitData>(m_agentCircuitsByUUID);
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseReaderLock();
            }
        }

        public void UpdateAgentData(AgentCircuitData agentData)
        {
            m_AgentCircuitsRwLock.AcquireReaderLock(-1);
            try
            {
                if (m_agentCircuits.ContainsKey((uint) agentData.circuitcode))
                {
                    m_agentCircuits[(uint) agentData.circuitcode].firstname = agentData.firstname;
                    m_agentCircuits[(uint) agentData.circuitcode].lastname = agentData.lastname;
                    m_agentCircuits[(uint) agentData.circuitcode].startpos = agentData.startpos;

                    // Updated for when we don't know them before calling Scene.NewUserConnection
                    m_agentCircuits[(uint) agentData.circuitcode].SecureSessionID = agentData.SecureSessionID;
                    m_agentCircuits[(uint) agentData.circuitcode].SessionID = agentData.SessionID;

                    // m_log.Debug("update user start pos is " + agentData.startpos.X + " , " + agentData.startpos.Y + " , " + agentData.startpos.Z);
                }
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseReaderLock();
            }
        }

        /// <summary>
        /// Sometimes the circuitcode may not be known before setting up the connection
        /// </summary>
        /// <param name="circuitcode"></param>
        /// <param name="newcircuitcode"></param>
        public bool TryChangeCiruitCode(uint circuitcode, uint newcircuitcode)
        {
            m_AgentCircuitsRwLock.AcquireWriterLock(-1);
            try
            {
                if (m_agentCircuits.ContainsKey((uint)circuitcode) && !m_agentCircuits.ContainsKey((uint)newcircuitcode))
                {
                    AgentCircuitData agentData = m_agentCircuits[(uint)circuitcode];

                    agentData.circuitcode = newcircuitcode;

                    m_agentCircuits.Remove((uint)circuitcode);
                    m_agentCircuits.Add(newcircuitcode, agentData);
                    return true;
                }
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseWriterLock();
            }

            return false;
        }

        public void UpdateAgentChildStatus(uint circuitcode, bool childstatus)
        {
            m_AgentCircuitsRwLock.AcquireReaderLock(-1);
            try
            {
                if (m_agentCircuits.ContainsKey(circuitcode))
                    m_agentCircuits[circuitcode].child = childstatus;
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseReaderLock();
            }
        }

        public bool GetAgentChildStatus(uint circuitcode)
        {
            m_AgentCircuitsRwLock.AcquireReaderLock(-1);
            try
            {
                if (m_agentCircuits.ContainsKey(circuitcode))
                    return m_agentCircuits[circuitcode].child;
            }
            finally
            {
                m_AgentCircuitsRwLock.ReleaseReaderLock();
            }

            return false;
        }
    }
}
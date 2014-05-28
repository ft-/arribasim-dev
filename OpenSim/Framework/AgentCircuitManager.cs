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
using System;
using System.Collections.Generic;
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
        private ThreadedClasses.RwLockedDoubleDictionary<uint, UUID, AgentCircuitData> m_agentCircuits = new ThreadedClasses.RwLockedDoubleDictionary<uint, UUID, AgentCircuitData>();

        public virtual AuthenticateResponse AuthenticateSession(UUID sessionID, UUID agentID, uint circuitcode)
        {
            AgentCircuitData validcircuit = null;
            AuthenticateResponse user = new AuthenticateResponse();
            try
            {
                validcircuit = m_agentCircuits[circuitcode];
            }
            catch(KeyNotFoundException)
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
            try
            {
                m_agentCircuits.Add(circuitCode, agentData.AgentID, agentData);
            }
            catch(ArgumentException)
            {
                m_agentCircuits.Remove(circuitCode);
                m_agentCircuits.Remove(agentData.AgentID);
                m_agentCircuits.Add(circuitCode, agentData.AgentID, agentData);
            }
        }

        public virtual void RemoveCircuit(uint circuitCode)
        {
            m_agentCircuits.Remove(circuitCode);
        }

        public virtual void RemoveCircuit(UUID agentID)
        {
            m_agentCircuits.Remove(agentID);
        }

        public AgentCircuitData GetAgentCircuitData(uint circuitCode)
        {
            AgentCircuitData agentCircuit = null;

            m_agentCircuits.TryGetValue(circuitCode, out agentCircuit);

            return agentCircuit;
        }

        public AgentCircuitData GetAgentCircuitData(UUID agentID)
        {
            AgentCircuitData agentCircuit = null;

            m_agentCircuits.TryGetValue(agentID, out agentCircuit);
            return agentCircuit;
        }

        /// <summary>
        /// Get all current agent circuits indexed by agent UUID.
        /// </summary>
        /// <returns></returns>
        public Dictionary<UUID, AgentCircuitData> GetAgentCircuits()
        {
            Dictionary<UUID, AgentCircuitData> val;
            m_agentCircuits.CopyTo(out val);
            return val;
        }

        public void UpdateAgentData(AgentCircuitData agentData)
        {
            AgentCircuitData circuit;
            try
            {
                circuit = m_agentCircuits[agentData.circuitcode];
            }
            catch(KeyNotFoundException)
            {
                return;
            }
            
            circuit.firstname = agentData.firstname;
            circuit.lastname = agentData.lastname;
            circuit.startpos = agentData.startpos;

            /* Updated for when we don't know them before calling Scene.NewUserConnection */
            circuit.SecureSessionID = agentData.SecureSessionID;
            circuit.SessionID = agentData.SessionID;
        }

        /// <summary>
        /// Sometimes the circuitcode may not be known before setting up the connection
        /// </summary>
        /// <param name="circuitcode"></param>
        /// <param name="newcircuitcode"></param>
        public bool TryChangeCiruitCode(uint circuitcode, uint newcircuitcode)
        {
            try
            {
                m_agentCircuits.ChangeKey(newcircuitcode, circuitcode);
                return true;
            }
            catch(Exception)
            {
                return false;
            }
        }

        public void UpdateAgentChildStatus(uint circuitcode, bool childstatus)
        {
            try
            {
                m_agentCircuits[circuitcode].child = childstatus;
            }
            catch(KeyNotFoundException)
            {
            }
        }

        public bool GetAgentChildStatus(uint circuitcode)
        {
            try
            { 
                return m_agentCircuits[circuitcode].child;
            }
            catch(KeyNotFoundException)
            {
                return false;
            }
        }
    }
}
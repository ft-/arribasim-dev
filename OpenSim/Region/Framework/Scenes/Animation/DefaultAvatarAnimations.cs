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
using System.Collections.Generic;
using System.Threading;
using System.Xml;

namespace OpenSim.Region.Framework.Scenes.Animation
{
    public class DefaultAvatarAnimations
    {
        public static readonly string DefaultAnimationsPath = "data/avataranimations.xml";

        private static Dictionary<string, UUID> m_AnimsUUID = new Dictionary<string, UUID>();
        private static Dictionary<UUID, string> m_AnimsNames = new Dictionary<UUID, string>();
        private static Dictionary<UUID, string> m_AnimStateNames = new Dictionary<UUID, string>();
        private static ReaderWriterLock m_AnimsRwLock = new ReaderWriterLock();

        static DefaultAvatarAnimations()
        {
            LoadAnimations(DefaultAnimationsPath);
        }

        public static Dictionary<string, UUID> AnimsUUID 
        {
            get
            {
                m_AnimsRwLock.AcquireReaderLock(-1);
                try
                {
                    return new Dictionary<string, UUID>(m_AnimsUUID);
                }
                finally
                {
                    m_AnimsRwLock.ReleaseReaderLock();
                }
            }
        }

        public static Dictionary<UUID, string> AnimsNames
        {
            get
            {
                m_AnimsRwLock.AcquireReaderLock(-1);
                try
                {
                    return new Dictionary<UUID, string>(m_AnimsNames);
                }
                finally
                {
                    m_AnimsRwLock.ReleaseReaderLock();
                }
            }
        }

        public static Dictionary<UUID, string> AnimStateNames
        {
            get
            {
                m_AnimsRwLock.AcquireReaderLock(-1);
                try
                {
                    return new Dictionary<UUID, string>(m_AnimStateNames);
                }
                finally
                {
                    m_AnimsRwLock.ReleaseReaderLock();
                }
            }
        }

        /// <summary>
        /// Load the default SL avatar animations.
        /// </summary>
        /// <returns></returns>
        private static void LoadAnimations(string path)
        {
            m_AnimsRwLock.AcquireWriterLock(-1);
            try
            {
                using (XmlTextReader reader = new XmlTextReader(path))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(reader);
                    //                if (doc.DocumentElement != null)
                    //                {
                    foreach (XmlNode nod in doc.DocumentElement.ChildNodes)
                    {
                        if (nod.Attributes["name"] != null)
                        {
                            string name = nod.Attributes["name"].Value;
                            UUID id = (UUID)nod.InnerText;
                            string animState = (string)nod.Attributes["state"].Value;

                            m_AnimsUUID.Add(name, id);
                            m_AnimsNames.Add(id, name);
                            if (animState != "")
                                m_AnimStateNames.Add(id, animState);

                        }
                    }
                }
            }
            finally
            {
                m_AnimsRwLock.ReleaseWriterLock();
            }
        }

        /// <summary>
        /// Get the default avatar animation with the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static UUID GetDefaultAnimation(string name)
        {
            m_AnimsRwLock.AcquireReaderLock(-1);
            try
            {
                if (m_AnimsUUID.ContainsKey(name))
                {

                    return m_AnimsUUID[name];
                }
            }
            finally
            {
                m_AnimsRwLock.ReleaseReaderLock();
            }

            return UUID.Zero;
        }

        /// <summary>
        /// Get the name of the animation given a UUID. If there is no matching animation
        ///    return the UUID as a string.
        /// </summary>
        public static string GetDefaultAnimationName(UUID uuid)
        {
            string ret = "unknown";
            m_AnimsRwLock.AcquireReaderLock(-1);
            try
            {
                if (m_AnimsUUID.ContainsValue(uuid))
                {
                    foreach (KeyValuePair<string, UUID> kvp in m_AnimsUUID)
                    {
                        if (kvp.Value == uuid)
                        {
                            ret = kvp.Key;
                            break;
                        }
                    }
                }
                else
                {
                    ret = uuid.ToString();
                }
            }
            finally
            {
                m_AnimsRwLock.ReleaseReaderLock();
            }
            return ret;
        }
    }
}
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

using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Capabilities.Handlers.GetAssets;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using System;
using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Region.ClientStack.LindenCaps
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GetAssetsModule")]
    public class GetAssetsModule : INonSharedRegionModule
    {
        private IAssetService m_assetService;

        private class CapsData
        {
            public string Name;
            public string CfgName;
            public string Url;
            public string RedirectUrl;
        }

        private CapsData[] m_CapsData = new CapsData[]
        {
            new CapsData{ Name = "GetTexture" },
            new CapsData{ Name = "GetMesh" },
            new CapsData{ Name = "GetMesh2" },
            new CapsData{ Name = "ViewerAsset", CfgName = "GetAsset" },
        };

        private bool m_enabled;

        private Scene m_scene;

        #region ISharedRegionModule Members

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["ClientStack.LindenCaps"];
            if (config == null)
            {
                return;
            }

            foreach (CapsData data in m_CapsData)
            {
                string cfgName = data.CfgName ?? data.Name;
                data.Url = config.GetString("Cap_" + cfgName, string.Empty);
                if (data.Url != string.Empty)
                {
                    m_enabled = true;
                    data.RedirectUrl = config.GetString(cfgName + "RedirectURL");
                }
            }
        }

        public void AddRegion(Scene s)
        {
            m_scene = s;
        }

        public void RemoveRegion(Scene s)
        {
            if (m_enabled)
            {
                m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
            }
            m_scene = null;
        }

        public void RegionLoaded(Scene s)
        {
            if (m_enabled)
            {
                m_assetService = m_scene.RequestModuleInterface<IAssetService>();
                m_scene.EventManager.OnRegisterCaps += RegisterCaps;
            }
        }

        public void PostInitialise()
        {
        }

        public void Close() { }

        public string Name { get { return "GetAssetsModule"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        public void RegisterCaps(UUID agentID, Caps caps)
        {
            GetAssetsHandler assethandler = null;
            GetAssetsHandler reuse_assethandler = null;

            foreach(CapsData data in m_CapsData)
            {
                if(data.Url == "localhost")
                {
                    if (string.IsNullOrEmpty(data.RedirectUrl))
                    {
                        if (reuse_assethandler == null)
                        {
                            reuse_assethandler = new GetAssetsHandler("/CAPS/" + UUID.Random() + "/", m_assetService, "GetAsset", agentID.ToString(), null);
                        }
                        assethandler = reuse_assethandler;
                    }
                    else
                    {
                        assethandler = new GetAssetsHandler("/CAPS/" + UUID.Random() + "/", m_assetService, data.Name, agentID.ToString(), data.RedirectUrl);
                    }
                    caps.RegisterHandler(
                        data.Name,
                        assethandler);
                }
                else
                {
                    IExternalCapsModule handler = m_scene.RequestModuleInterface<IExternalCapsModule>();
                    if (handler != null)
                    {
                        handler.RegisterExternalUserCapsHandler(agentID, caps, data.Name, data.Url);
                    }
                    else
                    {
                        caps.RegisterHandler(data.Name, data.Url);
                    }
                }
            }
        }

    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Xml;
using OpenMetaverse;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;
using OpenSim.Services.Connectors.Hypergrid;

[assembly: Addin("OpenSimProfile", "0.3")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace OpenSimProfile.Modules.OpenProfile
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class OpenProfileModule : IProfileModule, ISharedRegionModule
    {
        //
        // Log module
        //
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        //
        // Module vars
        //
        private List<Scene> m_Scenes = new List<Scene>();
        private string m_ProfileServer = "";
        private bool m_Enabled = true;

        IUserManagement m_uMan;
        IUserManagement UserManagementModule
        {
            get
            {
                if (m_uMan == null)
                    m_uMan = m_Scenes[0].RequestModuleInterface<IUserManagement>();
                return m_uMan;
            }
        }

        #region IRegionModuleBase implementation
        public void Initialise(IConfigSource config)
        {
            IConfig profileConfig = config.Configs["Profile"];

            if (profileConfig == null)
            {
                m_Enabled = false;
                return;
            }
            if (profileConfig.GetString("Module", "OpenSimProfile") != "OpenSimProfile")
            {
                m_Enabled = false;
                return;
            }

            m_ProfileServer = profileConfig.GetString("ProfileURL", "");
            if (m_ProfileServer == "")
            {
                m_Enabled = false;
                return;
            }

            m_log.Info("[PROFILE] OpenSimProfile module is active");
            m_Enabled = true;
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            // Take ownership of the IProfileModule service
            scene.RegisterModuleInterface<IProfileModule>(this);

            // Add our scene to our list...
            lock(m_Scenes)
            {
                m_Scenes.Add(scene);
            }
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.UnregisterModuleInterface<IProfileModule>(this);

            lock(m_Scenes)
            {
                m_Scenes.Remove(scene);
            }
        }

        public void RegionLoaded(Scene scene)
        {
            // Hook up events
            scene.EventManager.OnNewClient += OnNewClient;
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "ProfileModule"; }
        }
        #endregion

        private ScenePresence FindPresence(UUID clientID)
        {
            ScenePresence p;

            foreach (Scene s in m_Scenes)
            {
                p = s.GetScenePresence(clientID);
                if (p != null && !p.IsChildAgent)
                    return p;
            }
            return null;
        }

        /// New Client Event Handler
        private void OnNewClient(IClientAPI client)
        {
            // Subscribe to messages

            // Classifieds
            client.AddGenericPacketHandler("avatarclassifiedsrequest", HandleAvatarClassifiedsRequest);
            client.OnClassifiedInfoUpdate += ClassifiedInfoUpdate;
            client.OnClassifiedDelete += ClassifiedDelete;

            // Picks
            client.AddGenericPacketHandler("avatarpicksrequest", HandleAvatarPicksRequest);
            client.AddGenericPacketHandler("pickinforequest", HandlePickInfoRequest);
            client.OnPickInfoUpdate += PickInfoUpdate;
            client.OnPickDelete += PickDelete;

            // Notes
            client.AddGenericPacketHandler("avatarnotesrequest", HandleAvatarNotesRequest);
            client.OnAvatarNotesUpdate += AvatarNotesUpdate;

            //Profile
            client.OnRequestAvatarProperties += RequestAvatarProperties;
            client.OnUpdateAvatarProperties += UpdateAvatarProperties;
            client.OnAvatarInterestUpdate += AvatarInterestsUpdate;
            client.OnUserInfoRequest += UserPreferencesRequest;
            client.OnUpdateUserInfo += UpdateUserPreferences;
        }

        //
        // Make external XMLRPC request
        //
        private Hashtable GenericXMLRPCRequest(Hashtable ReqParams, string method, string server)
        {
            ArrayList SendParams = new ArrayList();
            SendParams.Add(ReqParams);

            // Send Request
            XmlRpcResponse Resp;
            try
            {
                XmlRpcRequest Req = new XmlRpcRequest(method, SendParams);
                Resp = Req.Send(server, 30000);
            }
            catch (WebException ex)
            {
                m_log.ErrorFormat("[PROFILE]: Unable to connect to Profile " +
                        "Server {0}.  Exception {1}", m_ProfileServer, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (SocketException ex)
            {
                m_log.ErrorFormat(
                        "[PROFILE]: Unable to connect to Profile Server {0}. Method {1}, params {2}. " +
                        "Exception {3}", m_ProfileServer, method, ReqParams, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (XmlException ex)
            {
                m_log.ErrorFormat(
                        "[PROFILE]: Unable to connect to Profile Server {0}. Method {1}, params {2}. " +
                        "Exception {3}", m_ProfileServer, method, ReqParams.ToString(), ex);
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            if (Resp.IsFault)
            {
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }
            Hashtable RespData = (Hashtable)Resp.Value;

            return RespData;
        }

        // Classifieds Handler
        public void HandleAvatarClassifiedsRequest(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            UUID targetID;
            UUID.TryParse(args[0], out targetID);

            // Can't handle NPC yet...
            ScenePresence p = FindPresence(targetID);

            if (null != p)
            {
                if (p.PresenceType == PresenceType.Npc)
                    return;
            }

            string serverURI = string.Empty;
            GetUserProfileServerURI(targetID, out serverURI);

            Hashtable ReqHash = new Hashtable();
            ReqHash["uuid"] = args[0];

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    method, serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            Dictionary<UUID, string> classifieds = new Dictionary<UUID, string>();

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                classifieds[new UUID(d["classifiedid"].ToString())] = d["name"].ToString();
            }

            remoteClient.SendAvatarClassifiedReply(new UUID(args[0]), classifieds);
        }

        // Classifieds Update
        public void ClassifiedInfoUpdate(UUID queryclassifiedID, uint queryCategory, string queryName, string queryDescription, UUID queryParcelID,
                                         uint queryParentEstate, UUID querySnapshotID, Vector3 queryGlobalPos, byte queryclassifiedFlags,
                                         int queryclassifiedPrice, IClientAPI remoteClient)
        {
            Hashtable ReqHash = new Hashtable();

            Scene s = (Scene) remoteClient.Scene;
            Vector3 pos = remoteClient.SceneAgent.AbsolutePosition;
            ILandObject land = s.LandChannel.GetLandObject(pos.X, pos.Y);

            if (land == null)
                ReqHash["parcelname"] = String.Empty;
            else
                ReqHash["parcelname"] = land.LandData.Name;

            ReqHash["creatorUUID"] = remoteClient.AgentId.ToString();
            ReqHash["classifiedUUID"] = queryclassifiedID.ToString();
            ReqHash["category"] = queryCategory.ToString();
            ReqHash["name"] = queryName;
            ReqHash["description"] = queryDescription;
            ReqHash["parentestate"] = queryParentEstate.ToString();
            ReqHash["snapshotUUID"] = querySnapshotID.ToString();
            ReqHash["sim_name"] = remoteClient.Scene.RegionInfo.RegionName;
            ReqHash["globalpos"] = queryGlobalPos.ToString();
            ReqHash["classifiedFlags"] = queryclassifiedFlags.ToString();
            ReqHash["classifiedPrice"] = queryclassifiedPrice.ToString();

            ScenePresence p = FindPresence(remoteClient.AgentId);


            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            Vector3 avaPos = p.AbsolutePosition;

            // Getting the parceluuid for this parcel
            ReqHash["parcelUUID"] = p.currentParcelUUID.ToString();

            // Getting the global position for the Avatar
            Vector3 posGlobal = new Vector3(remoteClient.Scene.RegionInfo.RegionLocX * Constants.RegionSize + avaPos.X,
                                            remoteClient.Scene.RegionInfo.RegionLocY * Constants.RegionSize + avaPos.Y,
                                            avaPos.Z);

            ReqHash["pos_global"] = posGlobal.ToString();

            //Check available funds if there is a money module present
            IMoneyModule money = s.RequestModuleInterface<IMoneyModule>();
            if (money != null)
            {
                if (!money.AmountCovered(remoteClient.AgentId, queryclassifiedPrice))
                {
                    remoteClient.SendCreateGroupReply(UUID.Zero, false, "Insufficient funds to create a classified ad.");
                    return;
                }
            }

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "classified_update", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
                return;
            }

            if (money != null && Convert.ToBoolean(result["created"]))
            {
                money.ApplyCharge(remoteClient.AgentId, queryclassifiedPrice,
                                  MoneyTransactionType.ClassifiedCharge,
                                  queryName);
            }
        }

        // Classifieds Delete
        public void ClassifiedDelete(UUID queryClassifiedID, IClientAPI remoteClient)
        {
            Hashtable ReqHash = new Hashtable();

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            ReqHash["classifiedID"] = queryClassifiedID.ToString();

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "classified_delete", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
            }
        }

        // Picks Handler
        public void HandleAvatarPicksRequest(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            UUID targetID;
            UUID.TryParse(args[0], out targetID);

            // Can't handle NPC yet...
            ScenePresence p = FindPresence(targetID);

            if (null != p)
            {
                if (p.PresenceType == PresenceType.Npc)
                    return;
            }

            string serverURI = string.Empty;
            GetUserProfileServerURI(targetID, out serverURI);

            Hashtable ReqHash = new Hashtable();
            ReqHash["uuid"] = args[0];

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    method, serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            Dictionary<UUID, string> picks = new Dictionary<UUID, string>();

            if (dataArray != null)
            {
                foreach (Object o in dataArray)
                {
                    Hashtable d = (Hashtable)o;

                    if (d["name"] == null)
                        picks[new UUID(d["pickid"].ToString())] = String.Empty;
                    else
                        picks[new UUID(d["pickid"].ToString())] = d["name"].ToString();
                }
            }

            remoteClient.SendAvatarPicksReply(new UUID(args[0]), picks);
        }

        // Picks Request
        public void HandlePickInfoRequest(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            Hashtable ReqHash = new Hashtable();

            UUID targetID;
            UUID.TryParse(args[0], out targetID);

            string serverURI = string.Empty;
            GetUserProfileServerURI(targetID, out serverURI);

            ReqHash["avatar_id"] = args[0];
            ReqHash["pick_id"] = args[1];

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    method, serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            Hashtable d = (Hashtable)dataArray[0];

            Vector3 globalPos = new Vector3();
            Vector3.TryParse(d["posglobal"].ToString(), out globalPos);

            if (d["name"] == null)
                d["name"] = String.Empty;

            if (d["description"] == null)
                d["description"] = String.Empty;

            if (d["originalname"] == null)
                d["originalname"] = String.Empty;

            remoteClient.SendPickInfoReply(
                    new UUID(d["pickuuid"].ToString()),
                    new UUID(d["creatoruuid"].ToString()),
                    Convert.ToBoolean(d["toppick"]),
                    new UUID(d["parceluuid"].ToString()),
                    d["name"].ToString(),
                    d["description"].ToString(),
                    new UUID(d["snapshotuuid"].ToString()),
                    d["user"].ToString(),
                    d["originalname"].ToString(),
                    d["simname"].ToString(),
                    globalPos,
                    Convert.ToInt32(d["sortorder"]),
                    Convert.ToBoolean(d["enabled"]));
        }

        // Picks Update
        public void PickInfoUpdate(IClientAPI remoteClient, UUID pickID, UUID creatorID, bool topPick, string name, string desc, UUID snapshotID, int sortOrder, bool enabled)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["agent_id"] = remoteClient.AgentId.ToString();
            ReqHash["pick_id"] = pickID.ToString();
            ReqHash["creator_id"] = creatorID.ToString();
            ReqHash["top_pick"] = topPick.ToString();
            ReqHash["name"] = name;
            ReqHash["desc"] = desc;
            ReqHash["snapshot_id"] = snapshotID.ToString();
            ReqHash["sort_order"] = sortOrder.ToString();
            ReqHash["enabled"] = enabled.ToString();
            ReqHash["sim_name"] = remoteClient.Scene.RegionInfo.RegionName;

            ScenePresence p = FindPresence(remoteClient.AgentId);

            Vector3 avaPos = p.AbsolutePosition;

            // Getting the parceluuid for this parcel
            ReqHash["parcel_uuid"] = p.currentParcelUUID.ToString();

            // Getting the global position for the Avatar
            Vector3 posGlobal = new Vector3(remoteClient.Scene.RegionInfo.RegionLocX * Constants.RegionSize + avaPos.X,
                                            remoteClient.Scene.RegionInfo.RegionLocY * Constants.RegionSize + avaPos.Y,
                                            avaPos.Z);

            ReqHash["pos_global"] = posGlobal.ToString();

            // Getting the owner of the parcel
            ReqHash["user"] = "";   //FIXME: Get avatar/group who owns parcel

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            // Do the request
            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "picks_update", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
            }
        }

        // Picks Delete
        public void PickDelete(IClientAPI remoteClient, UUID queryPickID)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["pick_id"] = queryPickID.ToString();

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "picks_delete", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
            }
        }

        // Notes Handler
        public void HandleAvatarNotesRequest(Object sender, string method, List<String> args)
        {
            string targetid;
            string notes = "";

            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            Hashtable ReqHash = new Hashtable();

            ReqHash["avatar_id"] = remoteClient.AgentId.ToString();
            ReqHash["uuid"] = args[0];

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    method, serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            if (dataArray != null && dataArray[0] != null)
            {
                Hashtable d = (Hashtable)dataArray[0];

                targetid = d["targetid"].ToString();
                if (d["notes"] != null)
                    notes = d["notes"].ToString();

                remoteClient.SendAvatarNotesReply(new UUID(targetid), notes);
            }
        }

        // Notes Update
        public void AvatarNotesUpdate(IClientAPI remoteClient, UUID queryTargetID, string queryNotes)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["avatar_id"] = remoteClient.AgentId.ToString();
            ReqHash["target_id"] = queryTargetID.ToString();
            ReqHash["notes"] = queryNotes;

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "avatar_notes_update", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
            }
        }

        // Standard Profile bits
        public void AvatarInterestsUpdate(IClientAPI remoteClient, uint wantmask, string wanttext, uint skillsmask, string skillstext, string languages)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["avatar_id"] = remoteClient.AgentId.ToString();
            ReqHash["wantmask"] = wantmask.ToString();
            ReqHash["wanttext"] = wanttext;
            ReqHash["skillsmask"] = skillsmask.ToString();
            ReqHash["skillstext"] = skillstext;
            ReqHash["languages"] = languages;

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "avatar_interests_update", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
            }
        }

        public void UserPreferencesRequest(IClientAPI remoteClient)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["avatar_id"] = remoteClient.AgentId.ToString();

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "user_preferences_request", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            if (dataArray != null && dataArray[0] != null)
            {
                Hashtable d = (Hashtable)dataArray[0];
                string mail = "";

                if (d["email"] != null)
                    mail = d["email"].ToString();

                remoteClient.SendUserInfoReply(
                        Convert.ToBoolean(d["imviaemail"]),
                        Convert.ToBoolean(d["visible"]),
                        mail);
            }
        }

        public void UpdateUserPreferences(bool imViaEmail, bool visible, IClientAPI remoteClient)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["avatar_id"] = remoteClient.AgentId.ToString();
            ReqHash["imViaEmail"] = imViaEmail.ToString();
            ReqHash["visible"] = visible.ToString();

            string serverURI = string.Empty;
            GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "user_preferences_update", serverURI);

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
            }
        }

        // Profile data like the WebURL
        private Hashtable GetProfileData(UUID userID)
        {
            Hashtable ReqHash = new Hashtable();

            // Can't handle NPC yet...
            ScenePresence p = FindPresence(userID);

            if (null != p)
            {
                if (p.PresenceType == PresenceType.Npc)
                {
                    Hashtable npc =new Hashtable();
                    npc["success"] = "false";
                    npc["errorMessage"] = "Presence is NPC. ";
                    return npc;
                }
            }

            ReqHash["avatar_id"] = userID.ToString();

            string serverURI = string.Empty;
            GetUserProfileServerURI(userID, out serverURI);

            // This is checking a friend on the home grid
            // Not HG friend
            if ( String.IsNullOrEmpty(serverURI))
            {
                Hashtable nop =new Hashtable();
                nop["success"] = "false";
                nop["errorMessage"] = "No Presence - foreign friend";
                return nop;

            }

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "avatar_properties_request", serverURI);

            ArrayList dataArray = (ArrayList)result["data"];

            if (dataArray != null && dataArray[0] != null)
            {
                Hashtable d = (Hashtable)dataArray[0];
                return d;
            }
            return result;
        }

        public void RequestAvatarProperties(IClientAPI remoteClient, UUID avatarID)
        {
            if ( String.IsNullOrEmpty(avatarID.ToString()) || String.IsNullOrEmpty(remoteClient.AgentId.ToString()))
            {
                // Looking for a reason that some viewers are sending null Id's
                m_log.InfoFormat("[PROFILE]: This should not happen remoteClient.AgentId {0} - avatarID {1}", remoteClient.AgentId, avatarID);
                return;
            }

            // Can't handle NPC yet...
            ScenePresence p = FindPresence(avatarID);

            if (null != p)
            {
                if (p.PresenceType == PresenceType.Npc)
                    return;
            }

            IScene s = remoteClient.Scene;
            if (!(s is Scene))
                return;

            Scene scene = (Scene)s;

            string serverURI = string.Empty;
            bool foreign = GetUserProfileServerURI(avatarID, out serverURI);

            UserAccount account = null;
            Dictionary<string,object> userInfo;

            if (!foreign)
            {
                account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, avatarID);
            }
            else
            {
                userInfo = new Dictionary<string, object>();
            }

            Byte[] charterMember = new Byte[1];
            string born = String.Empty;
            uint flags = 0x00;

            if (null != account)
            {
                if (account.UserTitle == "")
                {
                    charterMember[0] = (Byte)((account.UserFlags & 0xf00) >> 8);
                }
                else
                {
                    charterMember = Utils.StringToBytes(account.UserTitle);
                }

                born = Util.ToDateTime(account.Created).ToString(
                                  "M/d/yyyy", CultureInfo.InvariantCulture);
                flags = (uint)(account.UserFlags & 0xff);
            }
            else
            {
                if (GetUserProfileData(avatarID, out userInfo) == true)
                {
                    if ((string)userInfo["user_title"] == "")
                    {
                        charterMember[0] = (Byte)(((Byte)userInfo["user_flags"] & 0xf00) >> 8);
                    }
                    else
                    {
                        charterMember = Utils.StringToBytes((string)userInfo["user_title"]);
                    }

                    int val_born = (int)userInfo["user_created"];
                    born = Util.ToDateTime(val_born).ToString(
                                  "M/d/yyyy", CultureInfo.InvariantCulture);

                    // picky, picky
                    int val_flags = (int)userInfo["user_flags"];
                    flags = (uint)(val_flags & 0xff);
                }
            }

        Hashtable profileData = GetProfileData(avatarID);
        string profileUrl = string.Empty;
        string aboutText = String.Empty;
        string firstLifeAboutText = String.Empty;
        UUID image = UUID.Zero;
        UUID firstLifeImage = UUID.Zero;
        UUID partner = UUID.Zero;
        uint   wantMask = 0;
        string wantText = String.Empty;
        uint   skillsMask = 0;
        string skillsText = String.Empty;
        string languages = String.Empty;

        if (profileData["ProfileUrl"] != null)
        profileUrl = profileData["ProfileUrl"].ToString();
        if (profileData["AboutText"] != null)
        aboutText = profileData["AboutText"].ToString();
        if (profileData["FirstLifeAboutText"] != null)
        firstLifeAboutText = profileData["FirstLifeAboutText"].ToString();
        if (profileData["Image"] != null)
        image = new UUID(profileData["Image"].ToString());
        if (profileData["FirstLifeImage"] != null)
        firstLifeImage = new UUID(profileData["FirstLifeImage"].ToString());
        if (profileData["Partner"] != null)
        partner = new UUID(profileData["Partner"].ToString());

        // The PROFILE information is no longer stored in the user
        // account. It now needs to be taken from the XMLRPC
        //
        remoteClient.SendAvatarProperties(avatarID, aboutText,born,
              charterMember, firstLifeAboutText,
          flags,
              firstLifeImage, image, profileUrl, partner);

        //Viewer expects interest data when it asks for properties.
        if (profileData["wantmask"] != null)
        wantMask = Convert.ToUInt32(profileData["wantmask"].ToString());
        if (profileData["wanttext"] != null)
        wantText = profileData["wanttext"].ToString();

        if (profileData["skillsmask"] != null)
        skillsMask = Convert.ToUInt32(profileData["skillsmask"].ToString());
        if (profileData["skillstext"] != null)
        skillsText = profileData["skillstext"].ToString();

        if (profileData["languages"] != null)
        languages = profileData["languages"].ToString();

        remoteClient.SendAvatarInterestsReply(avatarID, wantMask, wantText,
                          skillsMask, skillsText, languages);
        }

        public void UpdateAvatarProperties(IClientAPI remoteClient, UserProfileData newProfile)
        {
            // if it's the profile of the user requesting the update, then we change only a few things.
            if (remoteClient.AgentId == newProfile.ID)
            {
                Hashtable ReqHash = new Hashtable();

                ReqHash["avatar_id"] = remoteClient.AgentId.ToString();
                ReqHash["ProfileUrl"] = newProfile.ProfileUrl;
                ReqHash["Image"] = newProfile.Image.ToString();
                ReqHash["AboutText"] = newProfile.AboutText;
                ReqHash["FirstLifeImage"] = newProfile.FirstLifeImage.ToString();
                ReqHash["FirstLifeAboutText"] = newProfile.FirstLifeAboutText;

                string serverURI = string.Empty;
                GetUserProfileServerURI(remoteClient.AgentId, out serverURI);

                Hashtable result = GenericXMLRPCRequest(ReqHash,
                        "avatar_properties_update", serverURI);

                if (!Convert.ToBoolean(result["success"]))
                {
                    remoteClient.SendAgentAlertMessage(
                            result["errorMessage"].ToString(), false);
                }

                RequestAvatarProperties(remoteClient, newProfile.ID);
            }
        }

        private bool GetUserProfileServerURI(UUID userID, out string serverURI)
        {
            IUserManagement uManage = UserManagementModule;

            if (!uManage.IsLocalGridUser(userID))
            {
                serverURI = uManage.GetUserServerURL(userID, "ProfileServerURI");
                // Is Foreign
                return true;
            }
            else
            {
                serverURI = m_ProfileServer;
                // Is local
                return false;
            }
        }

        //
        // Get the UserAccountBits
        //
        private bool GetUserProfileData(UUID userID, out Dictionary<string, object> userInfo)
        {
            IUserManagement uManage = UserManagementModule;
            Dictionary<string,object> info = new Dictionary<string, object>();


            if (!uManage.IsLocalGridUser(userID))
            {
                // Is Foreign
                string home_url = uManage.GetUserServerURL(userID, "HomeURI");

                if (String.IsNullOrEmpty(home_url))
                {
                    info["user_flags"] = 0;
                    info["user_created"] = 0;
                    info["user_title"] = "Unavailable";

                    userInfo = info;
                    return true;
                }

                UserAgentServiceConnector uConn = new UserAgentServiceConnector(home_url);

                Dictionary<string, object> account = uConn.GetUserInfo(userID);

                if (account.Count > 0)
                {
                    if (account.ContainsKey("user_flags"))
                        info["user_flags"] = account["user_flags"];
                    else
                        info["user_flags"] = "";

                    if (account.ContainsKey("user_created"))
                        info["user_created"] = account["user_created"];
                    else
                        info["user_created"] = "";

                    info["user_title"] = "HG Visitor";
                }
                else
                {
                   info["user_flags"] = 0;
                   info["user_created"] = 0;
                   info["user_title"] = "HG Visitor";
                }
                userInfo = info;
                return true;
            }
            else
            {
                // Is local
                Scene scene = m_Scenes[0];
                IUserAccountService uas = scene.UserAccountService;
                UserAccount account = uas.GetUserAccount(scene.RegionInfo.ScopeID, userID);

                info["user_flags"] = account.UserFlags;
                info["user_created"] = account.Created;

                if (!String.IsNullOrEmpty(account.UserTitle))
                    info["user_title"] = account.UserTitle;
                else
                    info["user_title"] = "";

                userInfo = info;

                return false;
            }
        }
    }
}

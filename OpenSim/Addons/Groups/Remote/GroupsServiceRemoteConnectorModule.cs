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
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace OpenSim.Groups
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GroupsServiceRemoteConnectorModule")]
    public class GroupsServiceRemoteConnectorModule : ISharedRegionModule, IGroupsServicesConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;
        private GroupsServiceRemoteConnector m_GroupsService;
        private IUserManagement m_UserManagement;
        private List<Scene> m_Scenes;

        #region constructors
        public GroupsServiceRemoteConnectorModule()
        {
        }

        public GroupsServiceRemoteConnectorModule(IConfigSource config, IUserManagement uman)
        {
            Init(config);
            m_UserManagement = uman;
        }
        #endregion

        private void Init(IConfigSource config)
        {
            m_GroupsService = new GroupsServiceRemoteConnector(config);
            m_Scenes = new List<Scene>();

        }

        #region ISharedRegionModule

        public void Initialise(IConfigSource config)
        {
            IConfig groupsConfig = config.Configs["Groups"];
            if (groupsConfig == null)
                return;

            if ((groupsConfig.GetBoolean("Enabled", false) == false)
                    || (groupsConfig.GetString("ServicesConnectorModule", string.Empty) != Name))
            {
                return;
            }

            Init(config);

            m_Enabled = true;
            m_log.DebugFormat("[Groups.RemoteConnector]: Initializing {0}", this.Name);
        }

        public string Name
        {
            get { return "Groups Remote Service Connector"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_log.DebugFormat("[Groups.RemoteConnector]: Registering {0} with {1}", this.Name, scene.RegionInfo.RegionName); 
            scene.RegisterModuleInterface<IGroupsServicesConnector>(this);
            m_Scenes.Add(scene);
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.UnregisterModuleInterface<IGroupsServicesConnector>(this);
            m_Scenes.Remove(scene);
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_UserManagement == null)
            {
                m_UserManagement = scene.RequestModuleInterface<IUserManagement>();
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        #endregion

        #region IGroupsServicesConnector

        public UUID CreateGroup(UUID RequestingAgentID, string name, string charter, bool showInList, UUID insigniaID, int membershipFee, bool openEnrollment, 
            bool allowPublish, bool maturePublish, UUID founderID, out string reason)
        {
            m_log.DebugFormat("[Groups.RemoteConnector]: Creating group {0}", name);

            ExtendedGroupRecord r = m_GroupsService.CreateGroup(RequestingAgentID.ToString(), name, charter, showInList, insigniaID,
                    membershipFee, openEnrollment, allowPublish, maturePublish, founderID, out reason);

            if(r != null)
            {
                return r.GroupID;
            }
            return UUID.Zero;
        }

        public bool UpdateGroup(string RequestingAgentID, UUID groupID, string charter, bool showInList, UUID insigniaID, int membershipFee, 
            bool openEnrollment, bool allowPublish, bool maturePublish, out string reason)
        {
            reason = string.Empty;
            return null != m_GroupsService.UpdateGroup(RequestingAgentID, groupID, charter, showInList, insigniaID, membershipFee, openEnrollment, allowPublish, maturePublish);
        }

        public ExtendedGroupRecord GetGroupRecord(string RequestingAgentID, UUID GroupID, string GroupName)
        {
            if (GroupID == UUID.Zero && (GroupName == null || GroupName != null && GroupName == string.Empty))
                return null;

            return m_GroupsService.GetGroupRecord(RequestingAgentID, GroupID, GroupName); 
        }

        public List<DirGroupsReplyData> FindGroups(string RequestingAgentID, string search)
        {
            // TODO!
            return m_GroupsService.FindGroups(RequestingAgentID, search);
        }

        public bool AddAgentToGroup(string RequestingAgentID, string AgentID, UUID GroupID, UUID RoleID, string token, out string reason)
        {
            string agentFullID = AgentID;
            m_log.DebugFormat("[Groups.RemoteConnector]: Add agent {0} to group {1}", agentFullID, GroupID);

            return null != m_GroupsService.AddAgentToGroup(RequestingAgentID, agentFullID, GroupID, RoleID, token, out reason);
        }

        public void RemoveAgentFromGroup(string RequestingAgentID, string AgentID, UUID GroupID)
        {
            m_GroupsService.RemoveAgentFromGroup(RequestingAgentID, AgentID, GroupID);
        }

        public void SetAgentActiveGroup(string RequestingAgentID, string AgentID, UUID GroupID)
        {
            m_GroupsService.SetAgentActiveGroup(RequestingAgentID, AgentID, GroupID);
        }

        public ExtendedGroupMembershipData GetAgentActiveMembership(string RequestingAgentID, string AgentID)
        {
            return m_GroupsService.GetMembership(RequestingAgentID, AgentID, UUID.Zero);
        }

        public ExtendedGroupMembershipData GetAgentGroupMembership(string RequestingAgentID, string AgentID, UUID GroupID)
        {
            return m_GroupsService.GetMembership(RequestingAgentID, AgentID, GroupID);
        }

        public List<GroupMembershipData> GetAgentGroupMemberships(string RequestingAgentID, string AgentID)
        {
            return m_GroupsService.GetMemberships(RequestingAgentID, AgentID);
        }

        public List<GroupMembershipData> GetAgentGroupMemberships(string RequestingAgentID, string AgentID, bool forceUpdate)
        {
            return m_GroupsService.GetMemberships(RequestingAgentID, AgentID);
        }

        GroupMembersData ConvertGroupMembersData(ExtendedGroupMembersData _m)
        {
            GroupMembersData m = new GroupMembersData();
            m.AcceptNotices = _m.AcceptNotices;
            m.AgentPowers = _m.AgentPowers;
            m.Contribution = _m.Contribution;
            m.IsOwner = _m.IsOwner;
            m.ListInProfile = _m.ListInProfile;
            m.OnlineStatus = _m.OnlineStatus;
            m.Title = _m.Title;

            string url = string.Empty, first = string.Empty, last = string.Empty, tmp = string.Empty;
            Util.ParseUniversalUserIdentifier(_m.AgentID, out m.AgentID, out url, out first, out last, out tmp);
            if (url != string.Empty)
                m_UserManagement.AddUser(m.AgentID, first, last, url);

            return m;
        }

        GroupRoleMembersData ConvertGroupRoleMembersData(ExtendedGroupRoleMembersData _rm)
        {
            GroupRoleMembersData rm = new GroupRoleMembersData();
            rm.RoleID = _rm.RoleID;

            string url = string.Empty, first = string.Empty, last = string.Empty, tmp = string.Empty;
            Util.ParseUniversalUserIdentifier(_rm.MemberID, out rm.MemberID, out url, out first, out last, out tmp);
            if (url != string.Empty)
                m_UserManagement.AddUser(rm.MemberID, first, last, url);

            return rm;
        }

        public List<GroupMembersData> GetGroupMembers(string RequestingAgentID, UUID GroupID)
        {
            List<ExtendedGroupMembersData> res = m_GroupsService.GetGroupMembers(RequestingAgentID, GroupID);
            if(null == res)
            {
                return null;
            }
            return res.ConvertAll<GroupMembersData>(new Converter<ExtendedGroupMembersData, GroupMembersData>(ConvertGroupMembersData));
        }

        public bool AddGroupRole(string RequestingAgentID, UUID groupID, UUID roleID, string name, string description, string title, ulong powers, out string reason)
        {
            return m_GroupsService.AddGroupRole(RequestingAgentID, groupID, roleID, name, description, title, powers, out reason);
        }

        public bool UpdateGroupRole(string RequestingAgentID, UUID groupID, UUID roleID, string name, string description, string title, ulong powers)
        {
            return m_GroupsService.UpdateGroupRole(RequestingAgentID, groupID, roleID, name, description, title, powers);
        }

        public void RemoveGroupRole(string RequestingAgentID, UUID groupID, UUID roleID)
        {
            m_GroupsService.RemoveGroupRole(RequestingAgentID, groupID, roleID);
        }

        public List<GroupRolesData> GetGroupRoles(string RequestingAgentID, UUID GroupID)
        {
            return m_GroupsService.GetGroupRoles(RequestingAgentID, GroupID);
        }

        public List<GroupRoleMembersData> GetGroupRoleMembers(string RequestingAgentID, UUID GroupID)
        {
            List<ExtendedGroupRoleMembersData> res = m_GroupsService.GetGroupRoleMembers(RequestingAgentID, GroupID);
            if (res != null && res.Count > 0)
                return res.ConvertAll<GroupRoleMembersData>(new Converter<ExtendedGroupRoleMembersData, GroupRoleMembersData>(ConvertGroupRoleMembersData));
            else
                return new List<GroupRoleMembersData>();
        }

        public void AddAgentToGroupRole(string RequestingAgentID, string AgentID, UUID GroupID, UUID RoleID)
        {
            m_GroupsService.AddAgentToGroupRole(RequestingAgentID, AgentID, GroupID, RoleID);
        }

        public void RemoveAgentFromGroupRole(string RequestingAgentID, string AgentID, UUID GroupID, UUID RoleID)
        {
            m_GroupsService.RemoveAgentFromGroupRole(RequestingAgentID, AgentID, GroupID, RoleID);
        }

        public List<GroupRolesData> GetAgentGroupRoles(string RequestingAgentID, string AgentID, UUID GroupID)
        {
            return m_GroupsService.GetAgentGroupRoles(RequestingAgentID, AgentID, GroupID); ;
        }

        public void SetAgentActiveGroupRole(string RequestingAgentID, string AgentID, UUID GroupID, UUID RoleID)
        {
            m_GroupsService.SetAgentActiveGroupRole(RequestingAgentID, AgentID, GroupID, RoleID);
        }

        public void UpdateMembership(string RequestingAgentID, string AgentID, UUID GroupID, bool AcceptNotices, bool ListInProfile)
        {
            m_GroupsService.UpdateMembership(RequestingAgentID, AgentID, GroupID, AcceptNotices, ListInProfile);
        }

        public bool AddAgentToGroupInvite(string RequestingAgentID, UUID inviteID, UUID groupID, UUID roleID, string agentID)
        {
            return m_GroupsService.AddAgentToGroupInvite(RequestingAgentID, inviteID, groupID, roleID, agentID);
        }

        public GroupInviteInfo GetAgentToGroupInvite(string RequestingAgentID, UUID inviteID)
        {
            return m_GroupsService.GetAgentToGroupInvite(RequestingAgentID, inviteID);
        }

        public void RemoveAgentToGroupInvite(string RequestingAgentID, UUID inviteID)
        {
            m_GroupsService.RemoveAgentToGroupInvite(RequestingAgentID, inviteID);
        }

        public bool AddGroupNotice(string RequestingAgentID, UUID groupID, UUID noticeID, string fromName, string subject, string message, 
            bool hasAttachment, byte attType, string attName, UUID attItemID, string attOwnerID)
        {
            GroupNoticeInfo notice = new GroupNoticeInfo();
            notice.GroupID = groupID;
            notice.Message = message;
            notice.noticeData = new ExtendedGroupNoticeData();
            notice.noticeData.AttachmentItemID = attItemID;
            notice.noticeData.AttachmentName = attName;
            notice.noticeData.AttachmentOwnerID = attOwnerID.ToString();
            notice.noticeData.AttachmentType = attType;
            notice.noticeData.FromName = fromName;
            notice.noticeData.HasAttachment = hasAttachment;
            notice.noticeData.NoticeID = noticeID;
            notice.noticeData.Subject = subject;
            notice.noticeData.Timestamp = (uint)Util.UnixTimeSinceEpoch();

            return m_GroupsService.AddGroupNotice(RequestingAgentID, groupID, noticeID, fromName, subject, message,
                            hasAttachment, attType, attName, attItemID, attOwnerID);
        }

        public GroupNoticeInfo GetGroupNotice(string RequestingAgentID, UUID noticeID)
        {
            return m_GroupsService.GetGroupNotice(RequestingAgentID, noticeID);
        }

        public List<ExtendedGroupNoticeData> GetGroupNotices(string RequestingAgentID, UUID GroupID)
        {
            return m_GroupsService.GetGroupNotices(RequestingAgentID, GroupID);
        }

        #endregion
    }

}

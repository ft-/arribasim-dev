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

namespace OpenSim.Framework
{
    public class InventoryFolderImpl : InventoryFolderBase
    {
//        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static readonly string PATH_DELIMITER = "/";

        /// <summary>
        /// Items that are contained in this folder
        /// </summary>
        public ThreadedClasses.RwLockedDictionary<UUID, InventoryItemBase> Items = new ThreadedClasses.RwLockedDictionary<UUID, InventoryItemBase>();

        /// <summary>
        /// Child folders that are contained in this folder
        /// </summary>
        protected ThreadedClasses.RwLockedDictionary<UUID, InventoryFolderImpl> m_childFolders = new ThreadedClasses.RwLockedDictionary<UUID, InventoryFolderImpl>();

        // Constructors
        public InventoryFolderImpl(InventoryFolderBase folderbase)
        {
            Owner = folderbase.Owner;
            ID = folderbase.ID;
            Name = folderbase.Name;
            ParentID = folderbase.ParentID;
            Type = folderbase.Type;
            Version = folderbase.Version;
        }

        public InventoryFolderImpl()
        {
        }

        /// <summary>
        /// Create a new subfolder.
        /// </summary>
        /// <param name="folderID"></param>
        /// <param name="folderName"></param>
        /// <param name="type"></param>
        /// <returns>The newly created subfolder.  Returns null if the folder already exists</returns>
        public InventoryFolderImpl CreateChildFolder(UUID folderID, string folderName, ushort type)
        {
            try
            {
                return m_childFolders.AddIfNotExists(folderID, delegate()
                {
                    InventoryFolderImpl subFold = new InventoryFolderImpl();
                    subFold.Name = folderName;
                    subFold.ID = folderID;
                    subFold.Type = (short)type;
                    subFold.ParentID = this.ID;
                    subFold.Owner = Owner;
                    return subFold;
                });
            }
            catch(ThreadedClasses.RwLockedDictionary<UUID, InventoryFolderImpl>.KeyAlreadyExistsException)
            {
                return null;
            }
        }

        /// <summary>
        /// Add a folder that already exists.
        /// </summary>
        /// <param name="folder"></param>
        public void AddChildFolder(InventoryFolderImpl folder)
        {
            folder.ParentID = ID;
            m_childFolders[folder.ID] = folder;
        }

        /// <summary>
        /// Does this folder contain the given child folder?
        /// </summary>
        /// <param name="folderID"></param>
        /// <returns></returns>
        public bool ContainsChildFolder(UUID folderID)
        {
            return m_childFolders.ContainsKey(folderID);
        }

        /// <summary>
        /// Get a child folder
        /// </summary>
        /// <param name="folderID"></param>
        /// <returns>The folder if it exists, null if it doesn't</returns>
        public InventoryFolderImpl GetChildFolder(UUID folderID)
        {
            InventoryFolderImpl folder = null;

            m_childFolders.TryGetValue(folderID, out folder);

            return folder;
        }

        /// <summary>
        /// Removes the given child subfolder.
        /// </summary>
        /// <param name="folderID"></param>
        /// <returns>
        /// The folder removed, or null if the folder was not present.
        /// </returns>
        public InventoryFolderImpl RemoveChildFolder(UUID folderID)
        {
            InventoryFolderImpl removedFolder = null;

            m_childFolders.Remove(folderID, out removedFolder);

            return removedFolder;
        }

        /// <summary>
        /// Delete all the folders and items in this folder.
        /// </summary>
        public void Purge()
        {
            m_childFolders.ForEach(delegate(InventoryFolderImpl folder)
            {
                folder.Purge();
            });

            m_childFolders.Clear();
            Items.Clear();
        }

        /// <summary>
        /// Returns the item if it exists in this folder or in any of this folder's descendant folders
        /// </summary>
        /// <param name="itemID"></param>
        /// <returns>null if the item is not found</returns>
        public InventoryItemBase FindItem(UUID itemID)
        {
            InventoryItemBase ret;
            if (Items.TryGetValue(itemID, out ret))
            {
                return ret;
            }

            try
            {
                foreach (InventoryFolderImpl folder in m_childFolders.Values)
                {
                    InventoryItemBase item = folder.FindItem(itemID);

                    if (item != null)
                    {
                        throw new ThreadedClasses.ReturnValueException<InventoryItemBase>(item);
                    }
                }
            }
            catch(ThreadedClasses.ReturnValueException<InventoryItemBase> e)
            {
                return e.Value;
            }

            return null;
        }

        public InventoryItemBase FindAsset(UUID assetID)
        {
            try
            {
                Items.ForEach(delegate(InventoryItemBase item)
                {
                    if (item.AssetID == assetID)
                        throw new ThreadedClasses.ReturnValueException<InventoryItemBase>(item);
                });
            }
            catch(ThreadedClasses.ReturnValueException<InventoryItemBase> e)
            {
                return e.Value;
            }

            try
            {
                m_childFolders.ForEach(delegate(InventoryFolderImpl folder)
                {
                    InventoryItemBase item = folder.FindAsset(assetID);

                    if (item != null)
                    {
                        throw new ThreadedClasses.ReturnValueException<InventoryItemBase>(item);
                    }
                });
            }
            catch(ThreadedClasses.ReturnValueException<InventoryItemBase> e)
            {
                return e.Value;
            }

            return null;
        }

        /// <summary>
        /// Deletes an item if it exists in this folder or any children
        /// </summary>
        /// <param name="folderID"></param>
        /// <returns></returns>
        public bool DeleteItem(UUID itemID)
        {
            if (Items.Remove(itemID))
            {
                return true;
            }

            try
            {
                m_childFolders.ForEach(delegate(InventoryFolderImpl folder)
                {
                    if(folder.DeleteItem(itemID))
                    {
                        throw new ThreadedClasses.ReturnValueException<bool>(true);
                    }
                });
            }
            catch(ThreadedClasses.ReturnValueException<bool>)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the folder requested if it is this folder or is a descendent of this folder.  The search is depth
        /// first.
        /// </summary>
        /// <returns>The requested folder if it exists, null if it does not.</returns>
        public InventoryFolderImpl FindFolder(UUID folderID)
        {
            if (folderID == ID)
                return this;

            try
            {
                m_childFolders.ForEach(delegate(InventoryFolderImpl folder)
                {
                    InventoryFolderImpl returnFolder = folder.FindFolder(folderID);

                    if (returnFolder != null)
                        throw new ThreadedClasses.ReturnValueException<InventoryFolderImpl>(returnFolder);
                });
            }
            catch(ThreadedClasses.ReturnValueException<InventoryFolderImpl> e)
            {
                return e.Value;
            }

            return null;
        }

        /// <summary>
        /// Look through all child subfolders for a folder marked as one for a particular asset type, and return it.
        /// </summary>
        /// <param name="type"></param>
        /// <returns>Returns null if no such folder is found</returns>
        public InventoryFolderImpl FindFolderForType(int type)
        {
            try
            {
                m_childFolders.ForEach(delegate(InventoryFolderImpl f)
                {
                    if (f.Type == type)
                        throw new ThreadedClasses.ReturnValueException<InventoryFolderImpl>(f);
                });
            }
            catch(ThreadedClasses.ReturnValueException<InventoryFolderImpl> e)
            {
                return e.Value;
            }

            return null;
        }

        /// <summary>
        /// Find a folder given a PATH_DELIMITER delimited path starting from this folder
        /// </summary>
        ///
        /// This method does not handle paths that contain multiple delimitors
        ///
        /// FIXME: We do not yet handle situations where folders have the same name.  We could handle this by some
        /// XPath like expression
        ///
        /// FIXME: Delimitors which occur in names themselves are not currently escapable.
        /// 
        /// <param name="path">
        /// The path to the required folder.
        /// It this is empty or consists only of the PATH_DELIMTER then this folder itself is returned.
        /// </param>
        /// <returns>null if the folder is not found</returns>
        public InventoryFolderImpl FindFolderByPath(string path)
        {
            if (path == string.Empty)
                return this;

            path = path.Trim();

            if (path == PATH_DELIMITER)
                return this;

            string[] components = path.Split(new string[] { PATH_DELIMITER }, 2, StringSplitOptions.None);

            try
            {
                m_childFolders.ForEach(delegate(InventoryFolderImpl folder)
                {
                    if (folder.Name == components[0])
                        if (components.Length > 1)
                            throw new ThreadedClasses.ReturnValueException<InventoryFolderImpl>(folder.FindFolderByPath(components[1]));
                        else
                            throw new ThreadedClasses.ReturnValueException<InventoryFolderImpl>(folder);
                });
            }
            catch(ThreadedClasses.ReturnValueException<InventoryFolderImpl> e)
            {
                return e.Value;
            }

            // We didn't find a folder with the given name
            return null;
        }

        /// <summary>
        /// Find an item given a PATH_DELIMITOR delimited path starting from this folder.
        ///
        /// This method does not handle paths that contain multiple delimitors
        ///
        /// FIXME: We do not yet handle situations where folders or items have the same name.  We could handle this by some
        /// XPath like expression
        ///
        /// FIXME: Delimitors which occur in names themselves are not currently escapable.
        /// </summary>
        /// <param name="path">
        /// The path to the required item.
        /// </param>
        /// <returns>null if the item is not found</returns>
        public InventoryItemBase FindItemByPath(string path)
        {
            string[] components = path.Split(new string[] { PATH_DELIMITER }, 2, StringSplitOptions.None);

            if (components.Length == 1)
            {
                try
                {
                    Items.ForEach(delegate(InventoryItemBase item)
                    {
                        if (item.Name == components[0])
                            throw new ThreadedClasses.ReturnValueException<InventoryItemBase>(item);
                    });
                }
                catch(ThreadedClasses.ReturnValueException<InventoryItemBase> e)
                {
                    return e.Value;
                }
            }
            else
            {
                try
                {
                    m_childFolders.ForEach(delegate(InventoryFolderImpl folder)
                    {
                        if (folder.Name == components[0])
                            throw new ThreadedClasses.ReturnValueException<InventoryItemBase>(folder.FindItemByPath(components[1]));
                    });
                }
                catch(ThreadedClasses.ReturnValueException<InventoryItemBase> e)
                {
                    return e.Value;
                }
            }

            // We didn't find an item or intermediate folder with the given name
            return null;
        }

        /// <summary>
        /// Return a copy of the list of child items in this folder.  The items themselves are the originals.
        /// </summary>
        public List<InventoryItemBase> RequestListOfItems()
        {
            List<InventoryItemBase> itemList = new List<InventoryItemBase>();

            Items.ForEach(delegate(InventoryItemBase item)
            {
                //                    m_log.DebugFormat(
                //                        "[INVENTORY FOLDER IMPL]: Returning item {0} {1}, OwnerPermissions {2:X}",
                //                        item.Name, item.ID, item.CurrentPermissions);

                itemList.Add(item);
            });

            //m_log.DebugFormat("[INVENTORY FOLDER IMPL]: Found {0} items", itemList.Count);

            return itemList;
        }

        /// <summary>
        /// Return a copy of the list of child folders in this folder.  The folders themselves are the originals.
        /// </summary>
        public List<InventoryFolderBase> RequestListOfFolders()
        {
            return new List<InventoryFolderBase>(m_childFolders.Values);
        }

        public List<InventoryFolderImpl> RequestListOfFolderImpls()
        {
            return new List<InventoryFolderImpl>(m_childFolders.Values); ;
        }

        /// <value>
        /// The total number of items in this folder and in the immediate child folders (though not from other
        /// descendants).
        /// </value>
        public int TotalCount
        {
            get
            {
                int total = Items.Count;

                foreach (InventoryFolderImpl folder in m_childFolders.Values)
                {
                    total = total + folder.TotalCount;
                }

                return total;
            }
        }
    }
}

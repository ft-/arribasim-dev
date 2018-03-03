// * Copyright (c) Contributors, http://opensimulator.org/, http://www.nsl.tuis.ac.jp/
// * See CONTRIBUTORS.TXT for a full list of copyright holders.
// *
// * Redistribution and use in source and binary forms, with or without
// * modification, are permitted provided that the following conditions are met:
// *	 * Redistributions of source code must retain the above copyright
// *	   notice, this list of conditions and the following disclaimer.
// *	 * Redistributions in binary form must reproduce the above copyright
// *	   notice, this list of conditions and the following disclaimer in the
// *	   documentation and/or other materials provided with the distribution.
// *	 * Neither the name of the OpenSim Project nor the
// *	   names of its contributors may be used to endorse or promote products
// *	   derived from this software without specific prior written permission.
// *
// * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
// * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
// * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// */

using log4net;
using Mono.Addins;
using Nini.Config;
using NSL.Network.XmlRpc;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using System;
using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;



[assembly: Addin("DTLNSLMoneyModule", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]



namespace OpenSim.Modules.Currency
{
	//
	public enum TransactionType : int
	{
		// One-Time Charges
		GroupCreate	 	= 1002,
		GroupJoin		= 1004,
		UploadCharge	= 1101,
		LandAuction		= 1102,
		ClassifiedCharge= 1103,
		// Recurrent Charges
		ParcelDirFee	= 2003,
		ClassifiedRenew = 2005,
		ScheduledFee	= 2900,
		// Inventory Transactions
		GiveInventory   = 3000,
		// Transfers Between Users
		ObjectSale		= 5000,
		Gift			= 5001,
		LandSale		= 5002,
		ReferBonus		= 5003,
		InvntorySale	= 5004,
		RefundPurchase	= 5005,
		LandPassSale	= 5006,
		DwellBonus		= 5007,
		PayObject		= 5008,
		ObjectPays		= 5009,
		BuyMoney		= 5010,
		MoveMoney		= 5011,
		// Group Transactions
		GroupLiability  = 6003,
		GroupDividend   = 6004,
		// Stipend Credits
		StipendPayment  = 10000
	}


/*
	public enum OpenMetaverse.MoneyTransactionType : int
	{
		None = 0,
		FailSimulatorTimeout = 1,
		FailDataserverTimeout = 2,
		ObjectClaim = 1000,
		LandClaim = 1001,
		GroupCreate = 1002,
		ObjectPublicClaim = 1003,
		GroupJoin = 1004,
		TeleportCharge = 1100,
		UploadCharge = 1101,
		LandAuction = 1102,
		ClassifiedCharge = 1103,
		ObjectTax = 2000,
		LandTax = 2001,
		LightTax = 2002,
		ParcelDirFee = 2003,
		GroupTax = 2004,
		ClassifiedRenew = 2005,
		GiveInventory = 3000,
		ObjectSale = 5000,
		Gift = 5001,
		LandSale = 5002,
		ReferBonus = 5003,
		InventorySale = 5004,
		RefundPurchase = 5005,
		LandPassSale = 5006,
		DwellBonus = 5007,
		PayObject = 5008,
		ObjectPays = 5009,
		GroupLandDeed = 6001,
		GroupObjectDeed = 6002,
		GroupLiability = 6003,
		GroupDividend = 6004,
		GroupMembershipDues = 6005,
		ObjectRelease = 8000,
		LandRelease = 8001,
		ObjectDelete = 8002,
		ObjectPublicDecay = 8003,
		ObjectPublicDelete = 8004,
		LindenAdjustment = 9000,
		LindenGrant = 9001,
		LindenPenalty = 9002,
		EventFee = 9003,
		EventPrize = 9004,
		StipendBasic = 10000,
		StipendDeveloper = 10001,
		StipendAlways = 10002,
		StipendDaily = 10003,
		StipendRating = 10004,
		StipendDelta = 10005
	}
*/



	// 
	[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "MoneyModule")]
	public class MoneyModule : IMoneyModule, ISharedRegionModule
	{
		#region Constant numbers and members.

		// Constant memebers   
		private const int MONEYMODULE_REQUEST_TIMEOUT = 10000;

        private bool m_DTLNSLEnabled = false;

		// Private data members.   
		private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		private bool  m_sellEnabled = false;

		private IConfigSource m_config;

		private string m_moneyServURL	 = string.Empty;
		public  BaseHttpServer HttpServer;

		private string m_certFilename	 = "";
		private string m_certPassword	 = "";
		private bool   m_checkServerCert = false;
		private X509Certificate2 m_cert	 = null;

		//
		private bool   m_use_web_settle	 = false;
		private string m_settle_url	  	 = "";
		private string m_settle_message  = "";
		private bool   m_settle_user  	 = false;


		/// <summary>   
		/// Scene dictionary indexed by Region Handle   
		/// </summary>   
        private ThreadedClasses.RwLockedDictionary<ulong, Scene> m_sceneList = new ThreadedClasses.RwLockedDictionary<ulong, Scene>();

		// Events  
		public event ObjectPaid OnObjectPaid;


		// Price
		private int   ObjectCount 			  = 0;
		private int   PriceEnergyUnit 		  = 0;
		private int   PriceGroupCreate 		  = 0;
		private int   PriceObjectClaim 		  = 0;
		private float PriceObjectRent 		  = 0f;
		private float PriceObjectScaleFactor  = 0f;
		private int   PriceParcelClaim 		  = 0;
		private int   PriceParcelRent		  = 0;
		private float PriceParcelClaimFactor  = 0f;
		private int   PricePublicObjectDecay  = 0;
		private int   PricePublicObjectDelete = 0;
		private int   PriceRentLight 		  = 0;
		private int   PriceUpload 			  = 0;
		private int   TeleportMinPrice 		  = 0;
		private float TeleportPriceExponent   = 0f;
		private float EnergyEfficiency 		  = 0f;

		#endregion


		//
		public void Initialise(Scene scene, IConfigSource source)
		{
			Initialise(source);
			if(source != null)
			{
				IConfig economyConfig = source.Configs["Economy"];

				if (economyConfig == null || economyConfig.GetString("EconomyModule", string.Empty)!=Name)
				{
					return;
				}
				AddRegion(scene);
			}
		}



		#region ISharedRegionModule interface

		public void Initialise(IConfigSource source)
		{
			//m_log.InfoFormat("[MONEY]: Initialise:");

			// Handle the parameters errors.
			if (source==null) return;

			try
			{
				m_config = source;

				// [Economy] section
				IConfig economyConfig = m_config.Configs["Economy"];

				if (economyConfig.GetString("EconomyModule")!=Name)
				{
					m_log.InfoFormat("[MONEY]: The DTL/NSL MoneyModule is disabled");
					return;
				}
				else
				{
					m_log.InfoFormat("[MONEY]: The DTL/NSL MoneyModule is enabled");
				}

				m_sellEnabled  = economyConfig.GetBoolean("SellEnabled", false);
				m_moneyServURL = economyConfig.GetString("CurrencyServer");

				// クライアント証明書
				m_certFilename = economyConfig.GetString("ClientCertFilename", "");
				m_certPassword = economyConfig.GetString("ClientCertPassword", "");
				if (m_certFilename!="") {
					m_cert = new X509Certificate2(m_certFilename, m_certPassword);
					m_log.InfoFormat("[MONEY]: Issue Authentication of Client. Cert File is " + m_certFilename);
				}

				// Settlement
				m_use_web_settle = economyConfig.GetBoolean("SettlementByWeb", false);
				m_settle_url	 = economyConfig.GetString ("SettlementURL", "");
				m_settle_message = economyConfig.GetString ("SettlementMessage", "");

				// Price
				PriceEnergyUnit 		= economyConfig.GetInt	("PriceEnergyUnit", 		100);
				PriceObjectClaim 		= economyConfig.GetInt	("PriceObjectClaim", 		10);
				PricePublicObjectDecay 	= economyConfig.GetInt	("PricePublicObjectDecay", 	4);
				PricePublicObjectDelete = economyConfig.GetInt	("PricePublicObjectDelete", 4);
				PriceParcelClaim 		= economyConfig.GetInt	("PriceParcelClaim", 		1);
				PriceParcelClaimFactor 	= economyConfig.GetFloat("PriceParcelClaimFactor", 	1f);
				PriceUpload 			= economyConfig.GetInt	("PriceUpload", 			0);
				PriceRentLight			= economyConfig.GetInt	("PriceRentLight",			5);
				PriceObjectRent 		= economyConfig.GetFloat("PriceObjectRent", 		1);
				PriceObjectScaleFactor 	= economyConfig.GetFloat("PriceObjectScaleFactor", 	10);
				PriceParcelRent 		= economyConfig.GetInt	("PriceParcelRent", 		1);
				PriceGroupCreate 		= economyConfig.GetInt	("PriceGroupCreate", 		0);
				TeleportMinPrice 		= economyConfig.GetInt	("TeleportMinPrice", 		2);
				TeleportPriceExponent 	= economyConfig.GetFloat("TeleportPriceExponent", 	2f);
				EnergyEfficiency 		= economyConfig.GetFloat("EnergyEfficiency", 		1);
                m_DTLNSLEnabled = true;
			}
			catch
			{
				m_log.ErrorFormat("[MONEY]: Initialise: Faile to read configuration file");
			}
		}



		public void AddRegion(Scene scene)
		{
			//m_log.InfoFormat("[MONEY]: AddRegion:");

			if (scene==null) return;

            if (!m_DTLNSLEnabled)
            {
                return;
            }

			scene.RegisterModuleInterface<IMoneyModule>(this);	// 競合するモジュールの排除

			if (m_sceneList.Count==0)
			{
				if (!string.IsNullOrEmpty(m_moneyServURL))
				{
					MainServer.Instance.AddXmlRPCHandler("OnMoneyTransfered", OnMoneyTransferedHandler);
					MainServer.Instance.AddXmlRPCHandler("UpdateBalance", BalanceUpdateHandler);
					MainServer.Instance.AddXmlRPCHandler("UserAlert", UserAlertHandler);
					MainServer.Instance.AddXmlRPCHandler("GetBalance", GetBalanceHandler);				// added
					MainServer.Instance.AddXmlRPCHandler("AddBankerMoney", AddBankerMoneyHandler);		// added
					MainServer.Instance.AddXmlRPCHandler("SendMoneyBalance", SendMoneyBalanceHandler);	// added
					//MainServer.Instance.AddXmlRPCHandler("UploadCovered", UploadCoveredHandler);		// added for Aurora-Sim
					//MainServer.Instance.AddXmlRPCHandler("UploadCharge", UploadChargeHandler);		// added for Aurora-Sim
				}
			}

			m_sceneList[scene.RegionInfo.RegionHandle] = scene;

			scene.EventManager.OnNewClient			+= OnNewClient;
			scene.EventManager.OnMakeRootAgent		+= OnMakeRootAgent;
			scene.EventManager.OnMakeChildAgent		+= MakeChildAgent;

			// for OpenSim
			scene.EventManager.OnMoneyTransfer		+= MoneyTransferAction;
			scene.EventManager.OnValidateLandBuy	+= ValidateLandBuy;
			scene.EventManager.OnLandBuy 			+= processLandBuy;
		}

	

		public void RemoveRegion(Scene scene)
		{
		}


		public void RegionLoaded(Scene scene)
		{
		}


		public Type ReplaceableInterface
		{
			//get { return typeof(IMoneyModule); }
			get { return null; }
		}


		public bool IsSharedModule
		{
			get { return true; }
		}


		public string Name
		{
			get { return "DTLNSLMoneyModule"; }
		}


		public void PostInitialise()
		{
		}


		public void Close()
		{
		}


		#endregion




		#region IMoneyModule interface.

		// for LSL llGiveMoney() function
		public bool ObjectGiveMoney(UUID objectID, UUID fromID, UUID toID, int amount)
		{
			//m_log.InfoFormat("[MONEY]: ObjectGiveMoney: LSL ObjectGiveMoney. UUID = {0}", objectID.ToString());

			if (!m_sellEnabled) return false;

			string objName = string.Empty;
			string avatarName = string.Empty;

			SceneObjectPart sceneObj = GetLocatePrim(objectID);
			if (sceneObj!=null)
			{
				objName = sceneObj.Name;
			}

			Scene scene = GetLocateScene(toID);
			if (scene!=null)
			{
				UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, toID);
				if (account!=null)
				{
					avatarName = account.FirstName + " " + account.LastName;
				}
			}

			bool   ret = false;
			string description = String.Format("Object {0} pays {1}", objName, avatarName);

			if (sceneObj.OwnerID==fromID)
			{
				ulong regionHandle = sceneObj.RegionHandle;
				if (GetLocateClient(fromID)!=null)
				{
					//ret = TransferMoney(fromID, toID, amount, (int)TransactionType.ObjectPays, objectID, regionHandle, description);
					ret = TransferMoney(fromID, toID, amount, (int)MoneyTransactionType.ObjectPays, objectID, regionHandle, description);
				}
				else
				{
					//ret = ForceTransferMoney(fromID, toID, amount, (int)TransactionType.ObjectPays, objectID, regionHandle, description);
					ret = ForceTransferMoney(fromID, toID, amount, (int)MoneyTransactionType.ObjectPays, objectID, regionHandle, description);
				}
			}

			return ret;
		}


		//
		public int UploadCharge
		{
			get { return PriceUpload; }
		}


		//
		public int GroupCreationCharge
		{
			get { return PriceGroupCreate; }
		}


		public int GetBalance(UUID agentID)
		{
			IClientAPI client = GetLocateClient(agentID);
			return QueryBalanceFromMoneyServer(client);
		}


		public bool UploadCovered(UUID agentID, int amount)
		{
			IClientAPI client = GetLocateClient(agentID);
			int balance = QueryBalanceFromMoneyServer(client);
			if (balance<amount) return false;
			return true;
		}


		public bool AmountCovered(UUID agentID, int amount)
		{
			IClientAPI client = GetLocateClient(agentID);
			int balance = QueryBalanceFromMoneyServer(client);
			if (balance<amount) return false;
			return true;
		}


		public void ApplyUploadCharge(UUID agentID, int amount, string text)
		{
			ulong region = GetLocateScene(agentID).RegionInfo.RegionHandle;
			PayMoneyCharge(agentID, amount, (int)MoneyTransactionType.UploadCharge, region, text);
		}


		//////////////////////////////////////////////////////////////////////
		// for OpenSim
		//

		/*
		public void ApplyCharge(UUID agentID, int amount, string text)
		{
			ApplyCharge(agentID, amount, TransactionType.GroupCreate, text);
		}
		*/
		public void ApplyCharge(UUID agentID, int amount, MoneyTransactionType type)
		{
			ApplyCharge(agentID, amount, type, string.Empty);
		}


		//////////////////////////////////////////////////////////////////////
		// for Aurora-Sim
		//

		public void ApplyCharge(UUID agentID, int amount, MoneyTransactionType type, string text)
		{
			ulong region = GetLocateScene(agentID).RegionInfo.RegionHandle;
			PayMoneyCharge(agentID, amount, (int)type, region, text);
		}


		public bool Transfer(UUID fromID, UUID toID, int regionHandle, int amount, MoneyTransactionType type, string text)
		{
			return TransferMoney(fromID, toID, amount, (int)type, UUID.Zero, (ulong)regionHandle, text);
		}


		public bool Transfer(UUID fromID, UUID toID, UUID objectID, int amount, MoneyTransactionType type, string text)
		{
			SceneObjectPart sceneObj = GetLocatePrim(objectID);
			if (sceneObj==null) return false;

			ulong regionHandle = sceneObj.ParentGroup.Scene.RegionInfo.RegionHandle;
			return TransferMoney(fromID, toID, amount, (int)type, objectID, (ulong)regionHandle, text);
		}


		#endregion




		#region MoneyModule event handlers

		// 
		private void OnNewClient(IClientAPI client)
		{
			client.OnEconomyDataRequest += OnEconomyDataRequest;
		}



		public void OnMakeRootAgent(ScenePresence agent)
		{
			//m_log.InfoFormat("[MONEY]: OnMakeRootAgent:");

			int balance = 0;
			IClientAPI client = agent.ControllingClient;

			LoginMoneyServer(client, out balance);
			//client.SendMoneyBalance(UUID.Zero, true, new byte[0], balance);
			client.SendMoneyBalance(UUID.Zero, true, new byte[0], balance, 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);

			client.OnMoneyBalanceRequest 	+= OnMoneyBalanceRequest;
			client.OnRequestPayPrice 		+= OnRequestPayPrice;
			client.OnObjectBuy				+= OnObjectBuy;				// for OpenSim 
			client.OnLogout 				+= ClientClosed;
			//client.OnMoneyTransferRequest	+= MoneyTransferRequest;	// for Aurora-Sim
		}	   



		// for OnClientClosed event
		private void ClientClosed(IClientAPI client)
		{
			if (client!=null)
			{
				LogoffMoneyServer(client);
			}
		}



		// for OnMoneyTransferRequest event  (for Aurora-Sim)
		private void MoneyTransferRequest(UUID sourceID, UUID destID, int amount, int transactionType, string description)
		{
			//m_log.InfoFormat("[MONEY]: MoneyTransferRequest: type = {0} {1} {2}", transactionType, amount, description);

			if (transactionType==(int)MoneyTransactionType.UploadCharge) return;
			EventManager.MoneyTransferArgs moneyEvent = new EventManager.MoneyTransferArgs(sourceID, destID, amount, transactionType, description);
			Scene scene = GetLocateScene(sourceID);
			MoneyTransferAction(scene, moneyEvent);
		}



		// for OnMoneyTransfer event  (for OpenSim)
		private void MoneyTransferAction(Object sender, EventManager.MoneyTransferArgs moneyEvent)
		{
			//m_log.InfoFormat("[MONEY]: MoneyTransferAction: type = {0}", moneyEvent.transactiontype);
		
			if (!m_sellEnabled) return;

			// Check the money transaction is necessary.   
			if (moneyEvent.sender==moneyEvent.receiver)
			{
				return;
			}

			UUID receiver = moneyEvent.receiver;
			if (moneyEvent.transactiontype==(int)MoneyTransactionType.PayObject)		// Pay for the object.   
			{
				SceneObjectPart sceneObj = GetLocatePrim(moneyEvent.receiver);
				if (sceneObj!=null)
				{
					receiver = sceneObj.OwnerID;
				}
				else
				{
					return;
				}
			}

			// Before paying for the object, save the object local ID for current transaction.
			UUID  objectID = UUID.Zero;
			ulong regionHandle = 0;

			if (sender is Scene)
			{
				Scene scene  = (Scene)sender;
				regionHandle = scene.RegionInfo.RegionHandle;

				if (moneyEvent.transactiontype==(int)MoneyTransactionType.PayObject)
				{
					objectID = scene.GetSceneObjectPart(moneyEvent.receiver).UUID;
				}
			}

			TransferMoney(moneyEvent.sender, receiver, moneyEvent.amount, moneyEvent.transactiontype, objectID, regionHandle, "OnMoneyTransfer event");
		}



		// for OnMakeChildAgent event
		private void MakeChildAgent(ScenePresence avatar)
		{
		}



		/**/
		// for OnValidateLandBuy event
		//		For Aurora-Sim, OnParcelBuy event function is already defined
		//		in OpenSim/Region/CoreModules/World/Land/ParcelManagementModule.cs
		private void ValidateLandBuy(Object sender, EventManager.LandBuyArgs landBuyEvent)
		{
			//m_log.InfoFormat("[MONEY]: ValidateLandBuy:");
			
			IClientAPI senderClient = GetLocateClient(landBuyEvent.agentId);
			if (senderClient!=null)
			{
				int balance = QueryBalanceFromMoneyServer(senderClient);
				if (balance >= landBuyEvent.parcelPrice)
				{
					lock(landBuyEvent)
					{
						landBuyEvent.economyValidated = true;
					}
				}
			}
		}



		/**/
		// for LandBuy even
		//		For Aurora-Sim, OnParcelBuy event function is already defined
		//		in OpenSim/Region/CoreModules/World/Land/ParcelManagementModule.cs
		private void processLandBuy(Object sender, EventManager.LandBuyArgs landBuyEvent)
		{
			//m_log.InfoFormat("[MONEY]: processLandBuy:");

			if (!m_sellEnabled) return;

			lock(landBuyEvent)
			{
				if (landBuyEvent.economyValidated==true && landBuyEvent.transactionID==0)
				{
					landBuyEvent.transactionID = Util.UnixTimeSinceEpoch();

					ulong parcelID = (ulong)landBuyEvent.parcelLocalID;
					UUID  regionID = UUID.Zero;
					if (sender is Scene) regionID = ((Scene)sender).RegionInfo.RegionID;

					if (TransferMoney(landBuyEvent.agentId, landBuyEvent.parcelOwnerID, 
									  landBuyEvent.parcelPrice, (int)MoneyTransactionType.LandSale, regionID, parcelID, "Land Purchase"))
					{
						landBuyEvent.amountDebited = landBuyEvent.parcelPrice;
					}
				}
			}
		}



		/**/
		// for OnObjectBuy event
		//		For Aurora-Sim, OnObjectBuy event function is already defined 
		//		in OpenSim/Region/CoreModules/World/Objects/BuySell/BuySellModule.cs
		public void OnObjectBuy(IClientAPI remoteClient, UUID agentID, UUID sessionID, 
								UUID groupID, UUID categoryID, uint localID, byte saleType, int salePrice)
		{
			//m_log.InfoFormat("[MONEY]: OnObjectBuy: agent = {0}, {1}", agentID, remoteClient.AgentId);

			// Handle the parameters error.   
			if (!m_sellEnabled) return;
			if (remoteClient==null || salePrice<0) return;		// for L$0 Sell

			// Get the balance from money server.   
			int balance = QueryBalanceFromMoneyServer(remoteClient);
			if (balance < salePrice)
			{
				remoteClient.SendAgentAlertMessage("Unable to buy now. You don't have sufficient funds", false);
				return;
			}

			Scene scene = GetLocateScene(remoteClient.AgentId);
			if (scene!=null)
			{
				SceneObjectPart sceneObj = scene.GetSceneObjectPart(localID);
				if (sceneObj!=null)
				{
					IBuySellModule mod = scene.RequestModuleInterface<IBuySellModule>();
					if (mod!=null)
					{
						UUID receiverId = sceneObj.OwnerID;
						ulong regionHandle = sceneObj.RegionHandle;
						bool ret = TransferMoney(remoteClient.AgentId, receiverId, salePrice,
												(int)MoneyTransactionType.PayObject, sceneObj.UUID, regionHandle, "Object Buy");
						if (ret)
						{
							mod.BuyObject(remoteClient, categoryID, localID, saleType, salePrice);
						}
					}
				}
				else
				{
					remoteClient.SendAgentAlertMessage("Unable to buy now. The object was not found", false);
					return;
				}
			}
		}



		/// <summary>   
		/// Sends the the stored money balance to the client   
		/// </summary>   
		/// <param name="client"></param>   
		/// <param name="agentID"></param>   
		/// <param name="SessionID"></param>   
		/// <param name="TransactionID"></param>   
		private void OnMoneyBalanceRequest(IClientAPI client, UUID agentID, UUID SessionID, UUID TransactionID)
		{
			//m_log.InfoFormat("[MONEY]: OnMoneyBalanceRequest:");

			if (client.AgentId==agentID && client.SessionId==SessionID)
			{
				int balance = -1;
				if (!string.IsNullOrEmpty(m_moneyServURL))
				{
					balance = QueryBalanceFromMoneyServer(client);
				}

				if (balance < 0)
				{
					client.SendAlertMessage("Fail to query the balance");
				}
				else
				{
					//client.SendMoneyBalance(TransactionID, true, new byte[0], balance);
					client.SendMoneyBalance(TransactionID, true, new byte[0], balance, 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
				}
			}
			else
			{
				client.SendAlertMessage("Unable to send your money balance");
			}
		}



		private void OnRequestPayPrice(IClientAPI client, UUID objectID)
		{
			//m_log.InfoFormat("[MONEY]: OnRequestPayPrice:");

			Scene scene = GetLocateScene(client.AgentId);
			if (scene==null) return;
			SceneObjectPart sceneObj = scene.GetSceneObjectPart(objectID);
			if (sceneObj==null) return;
			SceneObjectGroup group = sceneObj.ParentGroup;
			SceneObjectPart root = group.RootPart;

			client.SendPayPrice(objectID, root.PayPrice);
		}



		//
		//private void OnEconomyDataRequest(UUID agentId)
		private void OnEconomyDataRequest(IClientAPI user)
		{
			//m_log.InfoFormat("[MONEY]: OnEconomyDataRequest:");

			//IClientAPI user = GetLocateClient(agentId);

			if (user!=null)
			{
				//Scene s = GetLocateScene(user.AgentId);
				Scene s = (Scene)user.Scene;

				user.SendEconomyData(EnergyEfficiency, s.RegionInfo.ObjectCapacity, ObjectCount, PriceEnergyUnit, PriceGroupCreate,
									 PriceObjectClaim, PriceObjectRent, PriceObjectScaleFactor, PriceParcelClaim, PriceParcelClaimFactor,
									 PriceParcelRent, PricePublicObjectDecay, PricePublicObjectDelete, PriceRentLight, PriceUpload,
									 TeleportMinPrice, TeleportPriceExponent);
			}
		}


		#endregion



		#region MoneyModule XML-RPC Handler

		// "OnMoneyTransfered" RPC from MoneyServer
		public XmlRpcResponse OnMoneyTransferedHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: OnMoneyTransferedHandler:");

			bool ret = false;

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					//requestParam.Contains("receiverUUID") &&
					requestParam.Contains("clientSessionID") &&
					requestParam.Contains("clientSecureSessionID"))
				{
					UUID clientUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
					//UUID receiverUUID = UUID.Zero;
					//UUID.TryParse((string)requestParam["receiverUUID"], out receiverUUID);
					if (clientUUID!=UUID.Zero)
					{
						IClientAPI client = GetLocateClient(clientUUID);
						if (client!=null &&
							client.SessionId.ToString()==(string)requestParam["clientSessionID"] &&
							client.SecureSessionId.ToString()==(string)requestParam["clientSecureSessionID"])
						{
							if (requestParam.Contains("transactionType") &&
								requestParam.Contains("objectID") &&
								requestParam.Contains("amount"))
							{
								//m_log.InfoFormat("[MONEY]: OnMoneyTransferedHandler: type = {0}", requestParam["transactionType"]);
								if ((int)requestParam["transactionType"]==(int)MoneyTransactionType.PayObject)		// Pay for the object.
								{
									// Send notify to the client(viewer) for Money Event Trigger.   
									ObjectPaid handlerOnObjectPaid = OnObjectPaid;
									if (handlerOnObjectPaid!=null)
									{
										UUID objectID = UUID.Zero;
										UUID.TryParse((string)requestParam["objectID"], out objectID);
										handlerOnObjectPaid(objectID, clientUUID, (int)requestParam["amount"]);	// call Script Engine for LSL money()
									}
									ret = true;
								}
							}
						}
					}
				}
			}

			// Send the response to money server.
			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["success"] = ret;

			if (!ret)
			{
				m_log.ErrorFormat("[MONEY]: OnMoneyTransferedHandler: Transaction is failed. MoneyServer will rollback");
			}
			resp.Value = paramTable;

			return resp;
		}



		// "UpdateBalance" RPC from MoneyServer or Script
		public XmlRpcResponse BalanceUpdateHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: BalanceUpdateHandler:");

			bool ret = false;

			#region Update the balance from money server.

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					requestParam.Contains("clientSessionID") &&			// unable for Aurora-Sim
					requestParam.Contains("clientSecureSessionID"))
				{
					UUID clientUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
					if (clientUUID!=UUID.Zero)
					{
						IClientAPI client = GetLocateClient(clientUUID);
						if (client!=null &&
							client.SessionId.ToString()==(string)requestParam["clientSessionID"] &&		// unable for Aurora-Sim
							client.SecureSessionId.ToString()==(string)requestParam["clientSecureSessionID"])
						{
							if (requestParam.Contains("Balance"))
							{
								// Send notify to the client.   
								string msg = "";
								if (requestParam.Contains("Message")) msg = (string)requestParam["Message"];
								//client.SendMoneyBalance(UUID.Random(), true, Utils.StringToBytes(msg), (int)requestParam["Balance"]);
								client.SendMoneyBalance(UUID.Random(), true, Utils.StringToBytes(msg), (int)requestParam["Balance"],
																					0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
								ret = true;
							}
						}
					}
				}
			}

			#endregion

			// Send the response to money server.
			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["success"] = ret;

			if (!ret)
			{
				m_log.ErrorFormat("[MONEY]: BalanceUpdateHandler: Cannot update client balance from MoneyServer");
			}
			resp.Value = paramTable;

			return resp;
		}



		// "UserAlert" RPC from Script
		public XmlRpcResponse UserAlertHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: UserAlertHandler:");

			bool ret = false;

			#region confirm the request and show the notice from money server.

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					requestParam.Contains("clientSessionID") &&			// unable for Aurora-Sim
					requestParam.Contains("clientSecureSessionID"))
				{
					UUID clientUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
					if (clientUUID!=UUID.Zero)
					{
						IClientAPI client = GetLocateClient(clientUUID);
						if (client!=null &&
							client.SessionId.ToString()==(string)requestParam["clientSessionID"] &&		// unable for Aurora-Sim
							client.SecureSessionId.ToString()==(string)requestParam["clientSecureSessionID"])
						{
							if (requestParam.Contains("Description"))
							{
								string description = (string)requestParam["Description"];
								// Show the notice dialog with money server message.
							   	GridInstantMessage gridMsg = new GridInstantMessage(null, UUID.Zero, "MonyServer", new UUID(clientUUID.ToString()),
																	(byte)InstantMessageDialog.MessageFromAgent, description, false, new Vector3());
								client.SendInstantMessage(gridMsg);
								ret = true; 
							}
						}
					}
				}
			}
			//
			#endregion

			// Send the response to money server.
			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["success"] = ret;

			resp.Value = paramTable;
			return resp;
		}



		// "GetBalance" RPC from Script
		public XmlRpcResponse GetBalanceHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: GetBalanceHandler:");

			bool ret = false;
			int  balance = -1;

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					requestParam.Contains("clientSessionID") &&		// unable for Aurora-Sim
					requestParam.Contains("clientSecureSessionID"))
				{
					UUID clientUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
					if (clientUUID!=UUID.Zero)
					{
						IClientAPI client = GetLocateClient(clientUUID);
						if (client!=null &&
							client.SessionId.ToString()==(string)requestParam["clientSessionID"] &&		// unable for Aurora-Sim
							client.SecureSessionId.ToString()==(string)requestParam["clientSecureSessionID"])
						{
							balance = QueryBalanceFromMoneyServer(client);
						}
					}
				}
			}

			// Send the response to caller.
			if (balance<0) 
			{
				m_log.ErrorFormat("[MONEY]: GetBalanceHandler: GetBalance transaction is failed");
				ret = false;
			}

			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["success"] = ret;
			paramTable["balance"] = balance;
			resp.Value = paramTable;

			return resp;
		}



		// "AddBankerMoney" RPC from Script
		public XmlRpcResponse AddBankerMoneyHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: AddBankerMoneyHandler:");

			bool ret = false;

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					requestParam.Contains("clientSessionID") &&			// unable for Aurora-Sim
					requestParam.Contains("clientSecureSessionID"))
				{
					UUID bankerUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out bankerUUID);
					if (bankerUUID!=UUID.Zero)
					{
						IClientAPI client = GetLocateClient(bankerUUID);
						if (client!=null &&
							client.SessionId.ToString()==(string)requestParam["clientSessionID"] &&			// unable for Aurora-Sim
							client.SecureSessionId.ToString()==(string)requestParam["clientSecureSessionID"])
						{
							if (requestParam.Contains("amount"))
							{
								Scene scene = (Scene)client.Scene;
								int amount  = (int)requestParam["amount"];
								ret = AddBankerMoney(bankerUUID, amount, scene.RegionInfo.RegionHandle);

								if (m_use_web_settle && m_settle_user) 
								{
									ret = true;
									IDialogModule dlg = scene.RequestModuleInterface<IDialogModule>();
									if (dlg!=null) {
										dlg.SendUrlToUser(bankerUUID, "SYSTEM", UUID.Zero, UUID.Zero, false, m_settle_message, m_settle_url);
									}
								}
							}
						}
					}
				}
			}

			if (!ret) m_log.ErrorFormat("[MONEY]: AddBankerMoneyHandler: Add Banker Money transaction is failed");

			// Send the response to caller.
			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["settle"]  = false;
			paramTable["success"] = ret;

			if (m_use_web_settle && m_settle_user) paramTable["settle"] = true;
			resp.Value = paramTable;

			return resp;
		}



		// "SendMoneyBalance" RPC from Script
		public XmlRpcResponse SendMoneyBalanceHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: SendMoneyBalanceHandler:");

			bool ret = false;

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					requestParam.Contains("secretAccessCode"))
				{
					UUID clientUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
					if (clientUUID!=UUID.Zero)
					{
						if (requestParam.Contains("amount"))
						{
							int amount  = (int)requestParam["amount"];
							string secretCode = (string)requestParam["secretAccessCode"];
							string scriptIP   = remoteClient.Address.ToString();

							MD5 md5 = MD5.Create();
							byte[] code = md5.ComputeHash(ASCIIEncoding.Default.GetBytes(secretCode + "_" + scriptIP));
							string hash = BitConverter.ToString(code).ToLower().Replace("-","");
							//m_log.InfoFormat("[MONEY]: SendMoneyBalanceHandler: SecretCode: {0} + {1} = {2}", secretCode, scriptIP, hash);
							ret = SendMoneyBalance(clientUUID, amount, hash);
						}
					}
					else {
						m_log.ErrorFormat("[MONEY]: SendMoneyBalanceHandler: amount is missed");
					}
				}
				else {
					if (!requestParam.Contains("clientUUID")) {
						m_log.ErrorFormat("[MONEY]: SendMoneyBalanceHandler: clientUUID is missed");
					}
					if (!requestParam.Contains("secretAccessCode")) {
						m_log.ErrorFormat("[MONEY]: SendMoneyBalanceHandler: secretAccessCode is missed");
					}
				}
			}
			else {
				m_log.ErrorFormat("[MONEY]: SendMoneyBalanceHandler: Count is under 0");
			}

			if (!ret) m_log.ErrorFormat("[MONEY]: SendMoneyBalanceHandler: Send Money transaction is failed");

			// Send the response to caller.
			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["success"] = ret;

			resp.Value = paramTable;

			return resp;
		}



		/*
		// "UploadCovered" RPC for Aurora-Sim
		public XmlRpcResponse UploadCoveredHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: UploadCoveredHandler:");

			bool ret = false;

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					requestParam.Contains("clientSessionID") &&
					requestParam.Contains("clientSecureSessionID"))
				{
					UUID clientUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
					if (clientUUID!=UUID.Zero)
					{
						IClientAPI client = GetLocateClient(clientUUID);
						if (client!=null &&
							client.SessionId.ToString()==(string)requestParam["clientSessionID"] &&
							client.SecureSessionId.ToString()==(string)requestParam["clientSecureSessionID"])
						{
							ret = UploadCovered(client, UploadCharge);
						}
					}
				}
			}

			// Send the response to caller.
			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["success"] = ret;
			resp.Value = paramTable;

			return resp;
		}
		*/



		/*
		// "UploadCovered" RPC for Aurora-Sim
		public XmlRpcResponse UploadChargeHandler(XmlRpcRequest request, IPEndPoint remoteClient)
		{
			//m_log.InfoFormat("[MONEY]: UploadChargeHandler:");

			bool ret = false;

			if (request.Params.Count>0)
			{
				Hashtable requestParam = (Hashtable)request.Params[0];
				if (requestParam.Contains("clientUUID") &&
					requestParam.Contains("clientSessionID") &&
					requestParam.Contains("clientSecureSessionID"))
				{
					UUID clientUUID = UUID.Zero;
					UUID.TryParse((string)requestParam["clientUUID"], out clientUUID);
					if (clientUUID!=UUID.Zero)
					{
						IClientAPI client = GetLocateClient(clientUUID);
						if (client!=null &&
							client.SessionId.ToString()==(string)requestParam["clientSessionID"] &&
							client.SecureSessionId.ToString()==(string)requestParam["clientSecureSessionID"])
						{
							ApplyUploadCharge(clientUUID, UploadCharge, "Upload Asset");
							ret = true;
						}
					}
				}
			}

			// Send the response to caller.
			XmlRpcResponse resp   = new XmlRpcResponse();
			Hashtable paramTable  = new Hashtable();
			paramTable["success"] = ret;
			resp.Value = paramTable;

			return resp;
		}
		*/


		#endregion




		#region MoneyModule private help functions

		/// <summary>   
		/// Transfer the money from one user to another. Need to notify money server to update.   
		/// </summary>   
		/// <param name="amount">   
		/// The amount of money.   
		/// </param>   
		/// <returns>   
		/// return true, if successfully.   
		/// </returns>   
		private bool TransferMoney(UUID sender, UUID receiver, int amount, int type, UUID objectID, ulong regionHandle, string description)
		{
			//m_log.InfoFormat("[MONEY]: TransferMoney:");

			bool ret = false;
			IClientAPI senderClient = GetLocateClient(sender);

			// Handle the illegal transaction.   
			if (senderClient==null) // receiverClient could be null.
			{
				m_log.InfoFormat("[MONEY]: TransferMoney: Client {0} not found", sender.ToString());
				return false;
			}

			if (QueryBalanceFromMoneyServer(senderClient)<amount)
			{
				m_log.InfoFormat("[MONEY]: TransferMoney: No insufficient balance in client [{0}]", sender.ToString());
				return false;
			}

			#region Send transaction request to money server and parse the resultes.

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				// Fill parameters for money transfer XML-RPC.   
				Hashtable paramTable = new Hashtable();
				paramTable["senderID"] 				= sender.ToString();
				paramTable["receiverID"] 			= receiver.ToString();
				paramTable["senderSessionID"] 		= senderClient.SessionId.ToString();
				paramTable["senderSecureSessionID"] = senderClient.SecureSessionId.ToString();
				paramTable["transactionType"] 		= type;
				paramTable["objectID"] 				= objectID.ToString();
				paramTable["regionHandle"] 			= regionHandle.ToString();
				paramTable["amount"] 				= amount;
				paramTable["description"] 			= description;

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "TransferMoney");

				// Handle the return values from Money Server.  
				if (resultTable!=null && resultTable.Contains("success"))
				{
					if ((bool)resultTable["success"]==true)
					{
						ret = true;
					}
				}
				else m_log.ErrorFormat("[MONEY]: TransferMoney: Can not money transfer request from [{0}] to [{1}]", sender.ToString(), receiver.ToString());
			}
			else m_log.ErrorFormat("[MONEY]: TransferMoney: Money Server is not available!!");

			#endregion

			return ret;
		}



		/// <summary>   
		/// Force transfer the money from one user to another. 
		/// This function does not check sender login.
		/// Need to notify money server to update.   
		/// </summary>   
		/// <param name="amount">   
		/// The amount of money.   
		/// </param>   
		/// <returns>   
		/// return true, if successfully.   
		/// </returns>   
		private bool ForceTransferMoney(UUID sender, UUID receiver, int amount, int type, UUID objectID, ulong regionHandle, string description)
		{
			//m_log.InfoFormat("[MONEY]: ForceTransferMoney:");

			bool ret = false;

			#region Force send transaction request to money server and parse the resultes.

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				// Fill parameters for money transfer XML-RPC.   
				Hashtable paramTable = new Hashtable();
				paramTable["senderID"] 			 = sender.ToString();
				paramTable["receiverID"] 		 = receiver.ToString();
				paramTable["transactionType"] 	 = type;
				paramTable["objectID"] 			 = objectID.ToString();
				paramTable["regionHandle"] 		 = regionHandle.ToString();
				paramTable["amount"] 			 = amount;
				paramTable["description"] 		 = description;

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "ForceTransferMoney");

				// Handle the return values from Money Server.  
				if (resultTable!=null && resultTable.Contains("success"))
				{
					if ((bool)resultTable["success"]==true)
					{
						ret = true;
					}
				}
				else m_log.ErrorFormat("[MONEY]: ForceTransferMoney: Can not money force transfer request from [{0}] to [{1}]", sender.ToString(), receiver.ToString());
			}
			else m_log.ErrorFormat("[MONEY]: ForceTransferMoney: Money Server is not available!!");

			#endregion

			return ret;
		}



		/// <summary>   
		/// Add the money to banker avatar. Need to notify money server to update.   
		/// </summary>   
		/// <param name="amount">   
		/// The amount of money.  
		/// </param>   
		/// <returns>   
		/// return true, if successfully.   
		/// </returns>   
		private bool AddBankerMoney(UUID bankerID, int amount, ulong regionHandle)
		{
			//m_log.InfoFormat("[MONEY]: AddBankerMoney:");

			bool ret = false;
			m_settle_user = false;

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				// Fill parameters for money transfer XML-RPC.   
				Hashtable paramTable = new Hashtable();
				paramTable["bankerID"] 			= bankerID.ToString();
				paramTable["transactionType"] 	= (int)TransactionType.BuyMoney;
				paramTable["amount"] 			= amount;
				paramTable["regionHandle"] 		= regionHandle.ToString();
				paramTable["description"] 		= "Add Money to Avatar";

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "AddBankerMoney");

				// Handle the return values from Money Server.  
				if (resultTable!=null)
				{
					if (resultTable.Contains("success") && (bool)resultTable["success"]==true)
					{
						ret = true;
					}
					else 
					{
						if (resultTable.Contains("banker"))
						{
							m_settle_user = !(bool)resultTable["banker"]; // If avatar is not banker, Web Settlement is used.
							if (m_settle_user && m_use_web_settle) m_log.ErrorFormat("[MONEY]: AddBankerMoney: Avatar is not Banker. Web Settlemrnt is used.");
						}
						else m_log.ErrorFormat("[MONEY]: AddBankerMoney: Fail Message {0}", resultTable["message"]);
					}
				}
				else m_log.ErrorFormat("[MONEY]: AddBankerMoney: Money Server is not responce");
			}
			else m_log.ErrorFormat("[MONEY]: AddBankerMoney: Money Server is not available!!");

			return ret;
		}



		/// <summary>   
		/// Send the money to avatar. Need to notify money server to update.   
		/// </summary>   
		/// <param name="amount">   
		/// The amount of money.  
		/// </param>   
		/// <returns>   
		/// return true, if successfully.   
		/// </returns>   
		private bool SendMoneyBalance(UUID avatarID, int amount, string secretCode)
		{
			//m_log.InfoFormat("[MONEY]: SendMoneyBalance:");

			bool ret = false;

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				// Fill parameters for money transfer XML-RPC.   
				Hashtable paramTable = new Hashtable();
				paramTable["avatarID"] 		   = avatarID.ToString();
				paramTable["transactionType"]  = (int)MoneyTransactionType.ReferBonus;
				paramTable["amount"] 		   = amount;
				paramTable["secretAccessCode"] = secretCode;
				paramTable["description"] 	   = "Bonus to Avatar";

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "SendMoneyBalance");

				// Handle the return values from Money Server.  
				if (resultTable!=null && resultTable.Contains("success"))
				{
					if ((bool)resultTable["success"]==true)
					{
						ret = true;
					}
					else m_log.ErrorFormat("[MONEY]: SendMoneyBalance: Fail Message is {0}", resultTable["message"]);
				}
				else m_log.ErrorFormat("[MONEY]: SendMoneyBalance: Money Server is not responce");
			}
			else m_log.ErrorFormat("[MONEY]: SendMoneyBalance: Money Server is not available!!");

			return ret;
		}



		/// <summary>   
		/// Pay the money of charge.
		/// </summary>   
		/// <param name="amount">   
		/// The amount of money.   
		/// </param>   
		/// <returns>   
		/// return true, if successfully.   
		/// </returns>   
		private bool PayMoneyCharge(UUID sender, int amount, int type, ulong regionHandle, string description)
		{
			//m_log.InfoFormat("[MONEY]: PayMoneyCharge:");

			bool ret = false;
			IClientAPI senderClient = GetLocateClient(sender);

			// Handle the illegal transaction.   
			if (senderClient==null) // receiverClient could be null.
			{
				m_log.InfoFormat("[MONEY]: PayMoneyCharge: Client {0} is not found", sender.ToString());
				return false;
			}

			if (QueryBalanceFromMoneyServer(senderClient)<amount)
			{
				m_log.InfoFormat("[MONEY]: PayMoneyCharge: No insufficient balance in client [{0}]", sender.ToString());
				return false;
			}

			#region Send transaction request to money server and parse the resultes.

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				// Fill parameters for money transfer XML-RPC.   
				Hashtable paramTable = new Hashtable();
				paramTable["senderID"] 				= sender.ToString();
				paramTable["senderSessionID"] 		= senderClient.SessionId.ToString();
				paramTable["senderSecureSessionID"] = senderClient.SecureSessionId.ToString();
				paramTable["transactionType"] 		= type;
				paramTable["amount"] 				= amount;
				paramTable["regionHandle"] 			= regionHandle.ToString();
				paramTable["description"] 			= description;

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "PayMoneyCharge");

				// Handle the return values from Money Server.  
				if (resultTable!=null && resultTable.Contains("success"))
				{
					if ((bool)resultTable["success"]==true)
					{
						ret = true;
					}
				}
				else m_log.ErrorFormat("[MONEY]: PayMoneyCharge: Can not pay money of charge request from [{0}]", sender.ToString());
			}
			else m_log.ErrorFormat("[MONEY]: PayMoneyCharge: Money Server is not available!!");

			#endregion

			return ret;
		}



		/// <summary>   
		/// Login the money server when the new client login.
		/// </summary>   
		/// <param name="userID">   
		/// Indicate user ID of the new client.   
		/// </param>   
		/// <returns>   
		/// return true, if successfully.   
		/// </returns>   
		private bool LoginMoneyServer(IClientAPI client, out int balance)
		{
			//m_log.InfoFormat("[MONEY]: LoginMoneyServer:");

			bool ret = false;
			balance = 0;

			#region Send money server the client info for login.

			Scene scene = (Scene)client.Scene;
			string userName = string.Empty;

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				// Get the username for the login user.
				if (client.Scene is Scene)
				{
					if (scene!=null)
					{
						UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, client.AgentId);
						if (account!=null)
						{
							userName = account.FirstName + " " + account.LastName;
						}
					}
				}

				// Login the Money Server.   
				Hashtable paramTable = new Hashtable();
				paramTable["openSimServIP"] 		= scene.RegionInfo.ServerURI.Replace(scene.RegionInfo.InternalEndPoint.Port.ToString(), 
																						 scene.RegionInfo.HttpPort.ToString());
				paramTable["userName"] 				= userName;
				paramTable["clientUUID"] 			= client.AgentId.ToString();
				paramTable["clientSessionID"] 		= client.SessionId.ToString();
				paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "ClientLogin");

				// Handle the return result 
				if (resultTable!=null && resultTable.Contains("success"))
				{
					if ((bool)resultTable["success"]==true)
					{
						balance = (int)resultTable["clientBalance"];
						m_log.InfoFormat("[MONEY]: LoginMoneyServer: Client [{0}] login Money Server {1}", client.AgentId.ToString(), m_moneyServURL);
						ret = true;
					}
				}
				else m_log.ErrorFormat("[MONEY]: LoginMoneyServer: Unable to login Money Server {0} for client [{1}]", m_moneyServURL, client.AgentId.ToString());
			}
			else m_log.ErrorFormat("[MONEY]: LoginMoneyServer: Money Server is not available!!");

			#endregion

			return ret;
		}



		/// <summary>   
		/// Log off from the money server.   
		/// </summary>   
		/// <param name="userID">   
		/// Indicate user ID of the new client.   
		/// </param>   
		/// <returns>   
		/// return true, if successfully.   
		/// </returns>   
		private bool LogoffMoneyServer(IClientAPI client)
		{
			//m_log.InfoFormat("[MONEY]: LogoffMoneyServer:");

			bool ret = false;

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				// Log off from the Money Server.   
				Hashtable paramTable = new Hashtable();
				paramTable["clientUUID"] 			= client.AgentId.ToString();
				paramTable["clientSessionID"] 		= client.SessionId.ToString();
				paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "ClientLogout");
				// Handle the return result
				if (resultTable!=null && resultTable.Contains("success"))
				{
					if ((bool)resultTable["success"]==true)
					{
						ret = true;
					}
				}
			}

			return ret;
		}



		/// <summary>   
		/// Generic XMLRPC client abstraction   
		/// </summary>   
		/// <param name="ReqParams">Hashtable containing parameters to the method</param>   
		/// <param name="method">Method to invoke</param>   
		/// <returns>Hashtable with success=>bool and other values</returns>   
		private Hashtable genericCurrencyXMLRPCRequest(Hashtable reqParams, string method)
		{
			//m_log.InfoFormat("[MONEY]: genericCurrencyXMLRPCRequest:");

			if (reqParams.Count<=0 || string.IsNullOrEmpty(method)) return null;

			if (m_checkServerCert)
			{
				if (!m_moneyServURL.StartsWith("https://"))
				{
					m_log.InfoFormat("[MONEY]: genericCurrencyXMLRPCRequest: CheckServerCert is true, but protocol is not HTTPS. Please check INI file");
					//return null;
				}
			}
			else
			{
				if (!m_moneyServURL.StartsWith("https://") && !m_moneyServURL.StartsWith("http://"))
				{
					m_log.ErrorFormat("[MONEY]: genericCurrencyXMLRPCRequest: Invalid Money Server URL: {0}", m_moneyServURL);
					return null;
				}
			}


			ArrayList arrayParams = new ArrayList();
			arrayParams.Add(reqParams);
			XmlRpcResponse moneyServResp = null;
			try
			{
				NSLXmlRpcRequest moneyModuleReq = new NSLXmlRpcRequest(method, arrayParams);
				moneyServResp = moneyModuleReq.certSend(m_moneyServURL, m_cert, MONEYMODULE_REQUEST_TIMEOUT);
			}
			catch (Exception ex)
			{
				m_log.ErrorFormat("[MONEY]: genericCurrencyXMLRPCRequest: Unable to connect to Money Server {0}", m_moneyServURL);
				m_log.ErrorFormat("[MONEY]: genericCurrencyXMLRPCRequest: {0}", ex);

				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
				ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
				ErrorHash["errorURI"] = "";
				return ErrorHash;
			}

			if (moneyServResp.IsFault)
			{
				Hashtable ErrorHash = new Hashtable();
				ErrorHash["success"] = false;
				ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
				ErrorHash["errorURI"] = "";
				return ErrorHash;
			}

			Hashtable moneyRespData = (Hashtable)moneyServResp.Value;
			return moneyRespData;
		}



		private int QueryBalanceFromMoneyServer(IClientAPI client)
		{
			//m_log.InfoFormat("[MONEY]: QueryBalanceFromMoneyServer:");

			int ret = -1;

			#region Send the request to get the balance from money server for cilent.

			if (client!=null)
			{
				if (!string.IsNullOrEmpty(m_moneyServURL))
				{
					Hashtable paramTable = new Hashtable();
					paramTable["clientUUID"] 			= client.AgentId.ToString();
					paramTable["clientSessionID"] 		= client.SessionId.ToString();
					paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();

					// Generate the request for transfer.   
					Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "GetBalance");

					// Handle the return result
					if (resultTable!=null && resultTable.Contains("success"))
					{
						if ((bool)resultTable["success"]==true)
						{
							ret = (int)resultTable["clientBalance"];
						}
					}
				}

				if (ret < 0)
				{
					m_log.ErrorFormat("[MONEY]: QueryBalanceFromMoneyServer: Unable to query balance from Money Server {0} for client [{1}]", 
																					m_moneyServURL, client.AgentId.ToString());
				}
			}

			#endregion

			return ret;
		}



		//
		private EventManager.MoneyTransferArgs GetTransactionInfo(IClientAPI client, string transactionID)
		{
			//m_log.InfoFormat("[MONEY]: GetTransactionInfo:");

			EventManager.MoneyTransferArgs args = null;

			if (!string.IsNullOrEmpty(m_moneyServURL))
			{
				Hashtable paramTable = new Hashtable();
				paramTable["clientUUID"]			= client.AgentId.ToString();
				paramTable["clientSessionID"]		= client.SessionId.ToString();			
				paramTable["clientSecureSessionID"] = client.SecureSessionId.ToString();
				paramTable["transactionID"]			= transactionID;

				// Generate the request for transfer.   
				Hashtable resultTable = genericCurrencyXMLRPCRequest(paramTable, "GetTransaction");

				// Handle the return result
				if (resultTable!=null && resultTable.Contains("success"))
				{
					if ((bool)resultTable["success"]==true)
					{
						int amount  = (int)resultTable["amount"];
						int type	= (int)resultTable["type"];
						string desc = (string)resultTable["description"];
						UUID sender = UUID.Zero;
						UUID recver = UUID.Zero;
						UUID.TryParse((string)resultTable["sender"],   out sender);
						UUID.TryParse((string)resultTable["receiver"], out recver);
						args = new EventManager.MoneyTransferArgs(sender, recver, amount, type, desc);
					}
					else 
					{
						m_log.ErrorFormat("[MONEY]: GetTransactionInfo: GetTransactionInfo: Fail to Request. {0}", (string)resultTable["description"]);
					}
				}
				else 
				{
					m_log.ErrorFormat("[MONEY]: GetTransactionInfo: Invalid Response");
				}
			}
			else
			{
				m_log.ErrorFormat("[MONEY]: GetTransactionInfo: Invalid Money Server URL");
			}

			return args;
		}



		/// Locates a IClientAPI for the client specified   
		/// </summary>   
		/// <param name="AgentID"></param>   
		/// <returns></returns>   
		private IClientAPI GetLocateClient(UUID AgentID)
		{
			IClientAPI client = null;

			foreach (Scene _scene in m_sceneList.Values)
			{
				ScenePresence tPresence = (ScenePresence)_scene.GetScenePresence(AgentID);
				if (tPresence!=null && !tPresence.IsChildAgent)
				{
					IClientAPI rclient = tPresence.ControllingClient;
					if (rclient!=null)
					{
						client = rclient;
						break;
					}
				}
			}

			return client;
		}



		private Scene GetLocateScene(UUID AgentId)
		{
			Scene scene = null;

			foreach (Scene _scene in m_sceneList.Values)
			{
				ScenePresence tPresence = (ScenePresence)_scene.GetScenePresence(AgentId);
				if (tPresence!=null && !tPresence.IsChildAgent)
				{
					scene = _scene;
					break;
				}
			}

			return scene;
		}



		private SceneObjectPart GetLocatePrim(UUID objectID)
		{
			SceneObjectPart sceneObj = null;

			foreach (Scene _scene in m_sceneList.Values)
			{
				SceneObjectPart part = (SceneObjectPart)_scene.GetSceneObjectPart(objectID);
				if (part!=null)
				{
					sceneObj = part;
					break;
				}
			}

			return sceneObj;
		}


		#endregion
	}

}

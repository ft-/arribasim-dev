using log4net;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace OpenSim.Framework
{
    public static class ServicePointManagerTimeoutSupport
    {
        static bool isMonoCached = false;
        static bool isMono = true; /* be safe initially */
        static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        static bool IsPlatformMono
        {
            get 
            {
                if (!isMonoCached)
                {
                    isMono = Type.GetType("Mono.Runtime") != null;
                    isMonoCached = true;
                    m_log.Info("[MONO]: Mono Workaround for ServicePoint caching enabled");
                }
                return isMono;
            }
        }

        public static void ResetHosts()
        {
            try
            {
                if (!IsPlatformMono)
                {
                    return;
                }
                IDictionary servicePoints = (IDictionary)(typeof(ServicePointManager).GetField("servicePoints",
                    BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.GetField).GetValue(null));

                lock (servicePoints)
                {
                    foreach (ServicePoint removing in servicePoints.Values)
                    {
                        var hostLock = typeof(ServicePoint).GetField("hostE",
                            BindingFlags.NonPublic | BindingFlags.GetField |
                            BindingFlags.Instance).GetValue(removing);

                        lock (hostLock)
                        {
                            typeof(ServicePoint).GetField("host", BindingFlags.NonPublic |
                                BindingFlags.SetField | BindingFlags.Instance).SetValue(removing, null);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                /* be neutral outside */
                m_log.DebugFormat("[MONO]: ServicePoints clearing threw exception: {0}", e.GetType().FullName);
            }
        }
    }
}

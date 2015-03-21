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
                var servicePoints = (IDictionary)(typeof(ServicePointManager).GetField("servicePoints",
                    BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.GetField).GetValue(null));

                lock (servicePoints)
                {
                    var toRemove =
                        servicePoints
                        .Cast<DictionaryEntry>()
                        .Where(de => ((ServicePoint)de.Value).CurrentConnections == 0)
                        .Select(de => de.Value)
                        .ToList();

                    foreach (var removing in toRemove)
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
                m_log.DebugFormat("ServicePoints clearing threw exception: {0}", e.GetType().FullName);
            }
        }
    }
}

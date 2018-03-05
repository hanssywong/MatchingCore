using LogHelper;
using MatchingLib;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MatchingCore
{
    class MatchingCoreSetup
    {
        internal static MatchingCoreSetup Instance { get; } = new MatchingCoreSetup();
        internal CancellationTokenSource cts { get; } = new CancellationTokenSource();
        string LogDirectory { get { return ConfigurationManager.AppSettings["LogDirectory"]; } }
        //internal InMsmqObj inMsmq { get; } = new InMsmqObj(ConfigurationManager.AppSettings["InMsmqName"]);

        internal void Shutdown()
        {
            cts.Cancel();
            ProcessRequest.Instance.Shutdown();
        }

        //internal InMsmqObj outMsmq { get; } = new InMsmqObj(ConfigurationManager.AppSettings["OutMsmqName"]);

        internal void Init()
        {
            LibraryLogger.Instance.Init(LogDirectory, "MT4MgrApiAdminService", Encoding.Default, LibraryLogger.libLogLevel.Debug);
            ProcessRequest.Instance.Init();
        }
    }
}

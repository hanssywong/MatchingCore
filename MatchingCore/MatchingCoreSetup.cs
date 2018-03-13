using MatchingLib;
using NLogHelper;
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
            NLogger.Instance.Shutdown();
        }

        //internal InMsmqObj outMsmq { get; } = new InMsmqObj(ConfigurationManager.AppSettings["OutMsmqName"]);

        internal void Init()
        {
            NLogger.Instance.Init(LogDirectory, "MatchingCore", Encoding.Default, NLogger.LogLevel.Debug);
            ProcessRequest.Instance.Init();
        }
    }
}

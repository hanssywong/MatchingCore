using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace MatchingCore
{
    class Program
    {
        static void Main(string[] args)
        {
            if (ConfigurationManager.AppSettings.HasKeys() && ConfigurationManager.AppSettings["RunAsDebugConsole"] != null &&
                string.Compare(ConfigurationManager.AppSettings["RunAsDebugConsole"], "true", StringComparison.OrdinalIgnoreCase) == 0)
            {
                MatchingCoreSetup.Instance.Init();
                Console.ReadKey(true);
                MatchingCoreSetup.Instance.Shutdown();
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                new MatchingCoreService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}

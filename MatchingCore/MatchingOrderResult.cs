using MatchingLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MatchingCore
{
    internal class MatchingOrderResult : ProcessOrderResult
    {
        public List<TxOutput> txList { get; set; }
        public bool CanRecycle { get; set; } = true;
        public void ResetObj()
        {
            this.Success = false;
            this.CanRecycle = true;
            this.Comment = string.Empty;
        }
    }
}

using BaseHelper;
using MatchingLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MatchingCore
{
    /// <summary>
    /// Transaction Obj Pool
    /// </summary>
    internal class TxPool
    {
        static objPool<TxOutput> Pool { get; } = new objPool<TxOutput>(() => new TxOutput(), 100000);
        /// <summary>
        /// Single Thread only method
        /// </summary>
        /// <returns></returns>
        internal static TxOutput Checkout()
        {
            return Pool.Checkout();
        }
        /// <summary>
        /// Thread safe, check in a buffer
        /// </summary>
        /// <param name="tx"></param>
        internal static void CheckIn(TxOutput tx)
        {
            tx.ResetObj();
            Pool.Checkin(tx);
        }
    }
}

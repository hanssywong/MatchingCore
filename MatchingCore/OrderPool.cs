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
    /// Order Obj Pool
    /// </summary>
    internal class OrderPool
    {
        static objPool<Order> Pool { get; } = new objPool<Order>(() => new Order(), 2000000);
        /// <summary>
        /// Single Thread only method
        /// </summary>
        /// <returns></returns>
        internal static Order Checkout()
        {
            return Pool.Checkout();
        }
        /// <summary>
        /// Thread safe, check in a buffer
        /// </summary>
        /// <param name="order"></param>
        internal static void Checkin(Order order)
        {
            order.ResetObj();
            Pool.Checkin(order);
        }
    }
}

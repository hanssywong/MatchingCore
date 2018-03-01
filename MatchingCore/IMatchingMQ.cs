using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MatchingCore
{
    interface IMatchingMqResult
    {
        bool bSucces { get; set; }
        string Msg { get; set; }
    }
    interface IMatchingMQ<T>
    {
        IMatchingMqResult Enqueue(T obj);
        IMatchingMqResult Dequeue(T obj);
    }
}

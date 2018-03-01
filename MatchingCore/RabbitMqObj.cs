using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MatchingCore
{
    interface IRabbitMq
    {
    }
    internal class RabbitMqOut : IRabbitMq
    {
        private ConnectionFactory factory { get; } = new ConnectionFactory();
        private IConnection conn { get; set; }
        private IModel channel { get; set; }
        private string queueName { get; set; }

        internal RabbitMqOut(string uri, string queue_name)
        {
            factory.Uri = new Uri(uri);
            conn = factory.CreateConnection();
            channel = conn.CreateModel();
            queueName = queue_name;
        }

        internal void Enqueue(byte[] data)
        {
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            channel.BasicPublish(exchange: "",
                                 routingKey: queueName,
                                 basicProperties: properties,
                                 body: data);
        }
    }
    internal class RabbitMqIn : IRabbitMq
    {
        private ConnectionFactory factory { get; } = new ConnectionFactory();
        private IConnection conn { get; set; }
        private IModel channel { get; set; }
        private EventingBasicConsumer consumer { get; set; }
        private string queueName { get; set; }

        internal RabbitMqIn(string uri, string queue_name)
        {
            factory.Uri = new Uri(uri);
            conn = factory.CreateConnection();
            channel = conn.CreateModel();
            queueName = queue_name;
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            consumer = new EventingBasicConsumer(channel);
            //consumer.Received += (model, ea) =>
            //{
            //    var body = ea.Body;
            //    var message = Encoding.UTF8.GetString(body);
            //    Console.WriteLine(" [x] Received {0}", message);
            //};
        }

        internal void BindReceived(EventHandler<BasicDeliverEventArgs> handler)
        {
            consumer.Received += handler;
            channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        }

        internal void MsgFinished(BasicDeliverEventArgs ea)
        {
            channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        }
    }
}

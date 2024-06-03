using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace dotnetmqtt
{
    class Program
    {
        private const string clientId = "dotnetmqtt";
        private const string server = "10.0.0.25";
        private const string username = "mqusr";
        private const string password = "mqpasswd";
        //private const string topicPrefix = "arduino/pult/";
        private const string topicPrefix = "/alice/";

        public static Dictionary<string, Dictionary<string, string>> ValueMappings = new();
        public static Dictionary<string,TopicClass> TopicClasses = new();

        public static bool IsConnected = false;

        static void Main(string[] args)
        {
            LoadConfigJson(@"config.json");
            //LoadPultTplJson(@"C:\My\Pult\pult\pult.tpl.json");
            while (true)
            {
                try
                {
                    if (!IsConnected)
                        ConnectToMqttWithHandlers().Wait();
                }
                catch
                {
                    Debug.WriteLine("failed reconnect");
                }
                Task.Delay(TimeSpan.FromMinutes(5)).Wait();
            }

        }

        private static void LoadConfigJson(string configPath)
        {
            dynamic jObject = JObject.Parse(File.ReadAllText(configPath));
            foreach (var valueMapping in jObject.valuemappings)
            {
                var name = valueMapping.Name;
                //Console.WriteLine($"Processing mapping {name}");
                var mapping = new Dictionary<string, string>();
                ValueMappings[name] = mapping;
                foreach (JProperty map in valueMapping.Value)
                {
                    mapping.Add(map.Name, map.Value.ToString());
                }

            }

            foreach (JObject jTopic in jObject.topics)
            {
                var topicClass = JsonConvert.DeserializeObject<TopicClass>(jTopic.ToString());
                Console.WriteLine($"Processing topic {topicClass.Topic}");
                TopicClasses.Add(topicClass.Topic, topicClass);
            }
        }

        private static void LoadPultTplJson(string jsonPath)
        {
            dynamic jObject = JObject.Parse(File.ReadAllText(jsonPath));
            foreach (var pult in jObject.radio)
            {
                //Console.WriteLine($"Processing pult {pult.name}");
                foreach (var plug in pult.plugs)
                {
                    Console.WriteLine($"Processing plug {plug.fileprefix} {plug.codePrefix}{plug.codeOn} {plug.codePrefix}{plug.codeOff}");
                }

            }
        }

        static async Task ConnectToMqttWithHandlers()
        {
            var options = new MqttClientOptionsBuilder()
                .WithClientId(clientId)
                .WithTcpServer(server)
                .WithCredentials(username, password)
                .WithCleanSession()
                .Build();

            var mqttClient = new MqttFactory().CreateMqttClient();

            mqttClient.ApplicationMessageReceivedAsync += async e =>
                {
                    var topic = e.ApplicationMessage.Topic;
                    if (TopicClasses.TryGetValue(topic, out var topicClass))
                    {
                        var payloadBuf = e.ApplicationMessage.Payload;
                        var payload = System.Text.Encoding.UTF8.GetString(payloadBuf, 0, payloadBuf.Length);
                        Console.WriteLine($"Got \"{topic}\" Value: {payload}");
                        //var subtopic = topic.Substring(topicPrefix.Length);
                        string value = payload;
                        if (topicClass.ValueType == "map")
                        {
                            value = topicClass.ValueMapping[payload];
                        }
                        else
                        {
                            if (ValueMappings.TryGetValue(topicClass.ValueType, out var mapping))
                            {
                                value = mapping[payload];
                            }
                        }

                        var command = topicClass.Command;
                        command = command.Replace("@!@", value);
                        Console.WriteLine($"Running command: \"{command}\"");
                        await RunCommandAsync(command);
                    }
                };

            mqttClient.ConnectedAsync += e =>
            {
                IsConnected = true;
                Console.WriteLine("### CONNECTED WITH SERVER ###");
                return Task.CompletedTask;
                //await mqttClient.SubscribeAsync(topicPrefix+"+", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
                //await mqttClient.SubscribeAsync("+", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
            };

            mqttClient.DisconnectedAsync += async e =>
            {
                IsConnected = false;
                Console.WriteLine($"### Disconnected from server### Reason: {e.Reason.ToString()}");
                await Task.Delay(TimeSpan.FromSeconds(5));
                try
                {
                    await mqttClient.ConnectAsync(options, CancellationToken.None);
                }
                catch
                {
                    Debug.WriteLine("failed reconnect in disconnect");
                }
            };

            await mqttClient.ConnectAsync(options, CancellationToken.None);

            var mqttTopicFilters = TopicClasses.Select(x=>x.Value.Topic).Select(x=>new MqttTopicFilter(){Topic = x, QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce}).ToList();
            var options2 = new MqttClientSubscribeOptions();
            options2.TopicFilters.AddRange(mqttTopicFilters);

            await mqttClient.SubscribeAsync(options2);
            Console.WriteLine("### SUBSCRIBED ###");
            //await mqttClient.PublishAsync(@$"homeassistant/switch/0x00158d000365680c/switch/config", System.Text.Encoding.UTF8.GetBytes("qwe"));
        }

        public static Task<Process> RunCommandAsync(string command)
        {
            //Escape quotes
            command = command.Replace("\"", "\\\"");
            using (Process proc = new Process())
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c \" " + command + " \"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = false;
                proc.StartInfo.RedirectStandardError = false;
                proc.Start();

                return Task.FromResult(proc);
            }
        }
        
        //public async static Task RunCommandAsync(string command)
        //{
        //    var cancellationToken = new CancellationToken();
        //    var process = await RunCommandProcessAsync(command, cancellationToken);
        //    process.WaitForExitAsync()Wait(120000, cancellationToken);

            //ProcessStartInfo startInfo = new ProcessStartInfo() { 
            //    FileName = "/bin/bash", 
            //    Arguments = command,   
            //    RedirectStandardOutput = true,
            //    RedirectStandardError = true,
            //    UseShellExecute = false,
            //}; 
            //Process proc = new Process() { StartInfo = startInfo, };
            //proc.Start();
            //while (!proc.StandardOutput.EndOfStream)
            //{
            //    Console.WriteLine(proc.StandardOutput.ReadLine());
            //}

            //proc.WaitForExit();
        //}

        public static string RunCommand(string command)
        {
            //Escape quotes
            command = command.Replace("\"", "\\\"");
            string result = "";
            using (System.Diagnostics.Process proc = new System.Diagnostics.Process())
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c \" " + command + " \"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.Start();

                //result += proc.StandardOutput.ReadToEnd();
                //result += proc.StandardError.ReadToEnd();
                //proc.WaitForExit();
            }
            return result;
            //ProcessStartInfo startInfo = new ProcessStartInfo() { 
            //    FileName = "/bin/bash", 
            //    Arguments = command,   
            //    RedirectStandardOutput = true,
            //    RedirectStandardError = true,
            //    UseShellExecute = false,
            //}; 
            //Process proc = new Process() { StartInfo = startInfo, };
            //proc.Start();
            //while (!proc.StandardOutput.EndOfStream)
            //{
            //    Console.WriteLine(proc.StandardOutput.ReadLine());
            //}

            //proc.WaitForExit();
        }
    }
}

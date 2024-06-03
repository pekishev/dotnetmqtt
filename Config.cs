using System.Collections.Generic;

namespace dotnetmqtt
{
    public class Config
    {
        public string[] ValueMappings;
    }




    public class TopicClass
    {
        public string Topic;
        public string Command;
        public string ValueType;
        public Dictionary<string,string> ValueMapping;
    }
}
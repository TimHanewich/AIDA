using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIDA
{
    //Basic implementation of ConversationTurn
    public class ConversationTurn
    {
        public bool From {get; set;}           //false = user, true = AIDA (AI)
        public DateTimeOffset At {get; set;}   //Exactly when
        public string Content {get; set;}      //Content of what was sent

        public ConversationTurn()
        {
            From = false;
            At = DateTimeOffset.Now;
            Content = string.Empty;
        }

        public JObject Pack()
        {
            JObject ToReturn = new JObject();
            
            //From?
            if (From == false)
            {
                ToReturn.Add("from", "user");
            }
            else if (From == true)
            {
                ToReturn.Add("from", "aida");
            }

            //At
            ToReturn.Add("at", At);

            //Content
            ToReturn.Add("content", Content);

            return ToReturn;
        }
    }
}
using System;

namespace AIDA
{
    public class AIDATools
    {
        public static string ConfigDirectoryPath
        {
            get
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIDA");
            }
        }

        public static string CustomPromptPath
        {
            get
            {
                string ConfigDir = ConfigDirectoryPath;
                string prompt_path = Path.Combine(ConfigDir, "prompt.md");
                if (System.IO.File.Exists(prompt_path) == false) //if it doesnt exist, try to create a blank one
                {
                   try
                    {
                        File.Create(prompt_path).Close();
                    }
                    catch
                    {
                        
                    } 
                }
                return prompt_path;
            }
        }

        public static string GetSystemPrompt()
        {
            List<string> SystemMessage = new List<string>();

            //System-injected prompt (AIDA info)
            SystemMessage.Add("You are AIDA, Artificial Intelligence Desktop Assistant. Your role is to be a friendly and helpful assistant. Speak in a playful, lighthearted, and fun manner.");
            SystemMessage.Add("Do not use emojis.");

            //Is there a custom prompt in prompt.md?
            if (System.IO.File.Exists(CustomPromptPath))
            {
                string custom_prompt = System.IO.File.ReadAllText(CustomPromptPath);
                if (custom_prompt != string.Empty)
                {
                    SystemMessage.Add(custom_prompt);
                }
            }

            //Construct as one.
            string sysmsg = "";
            foreach (string s in SystemMessage)
            {
                sysmsg = sysmsg + s + "\n\n";
            }
            sysmsg = sysmsg.Substring(0, sysmsg.Length - 2);

            return sysmsg;
        }
    }
}
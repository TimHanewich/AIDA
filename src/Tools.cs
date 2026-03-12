using System;

namespace AIDA
{
    public class Tools
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

        public static string MSXPath
        {
            get
            {
                string ConfigDir = ConfigDirectoryPath;
                string msx_path = Path.Combine(ConfigDir, "msx.txt");
                if (System.IO.File.Exists(msx_path) == false) //if it doesnt exist, try to create a blank one
                {
                   try
                    {
                        File.Create(msx_path).Close();
                    }
                    catch
                    {
                        
                    } 
                }
                return msx_path;
            }
        }

        public static string GetMSXCookie()
        {
            string path = MSXPath;
            if (System.IO.File.Exists(path))
            {
                string content = System.IO.File.ReadAllText(path);
                return content;
            }
            return "";
        }

        public static string GetSystemPrompt()
        {
            List<string> SystemMessage = new List<string>();

            //System-injected prompt (AIDA info)
            SystemMessage.Add("You are AIDA, Artificial Intelligence Desktop Assistant. Your role is to be a friendly and helpful assistant. Speak in a playful, lighthearted, and fun manner.");
            SystemMessage.Add("Do not use emojis.");

            //MSX Info
            string msx = @"You are an expert on interfacing with MSX, Microsoft's CRM system.

The basic table structure you should be aware of:
systemuser = a list of all users (sellers) within MSX. The `fullname` property lists their name.
account = list of all accounts within MSX (customers).
opportunity = a list of all logged opportunities with accounts within MSX, each tied to an account.
task = a list of all tasks tied to an opportunity OR account (polymorphous) of activities a technical sales engineer has completed.
team = a 'deal team' tied to a particular opportunity. The team that is working to close this opportunity.
teammembership = a bridge table that associates a systemuser with a team (many to many).

Often times the user will ask you to find someone's opportunities. This does not necessarily mean opportunities that they OWN, but rather that they are working on. Here is how to find that:
1. Search for them in the systemuser table (fullname field). This will give you their unique ID.
2. Find a list of all teammemberships that they are associated with.
3. For each of those teammemberships, find what 'deal team' it ties back to (team table)
4. For each team, query the opportunity it corresponds to.
";
            SystemMessage.Add(msx);

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
using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class ExploreDirectoryTool : ExecutableFunction
    {
        public ExploreDirectoryTool()
        {
            Name = "explore_directory";
            Description = "Explore the contents of a directory (folder) on the user's computer, listing out all files and sub-directories contained in it.";
            InputParameters.Add(new FunctionInputParameter("path", "The path of the directory to explore."));
        }

        public override Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string? path = null;
            if (arguments != null)
            {
                JProperty? prop = arguments.Property("path");
                if (prop != null) path = prop.Value.ToString();
            }

            if (path == null)
            {
                return Task.FromResult("You must provide the 'path' parameter!");
            }
            if (System.IO.Directory.Exists(path) == false)
            {
                return Task.FromResult("Directory at '" + path + "' does not exist!");
            }

            AnsiConsole.Markup("[gray][italic]exploring '" + Markup.Escape(path) + "'... [/][/]");
            try
            {
                List<string> entries = new List<string>();

                string[] dirs = System.IO.Directory.GetDirectories(path);
                foreach (string d in dirs)
                {
                    entries.Add("[DIR] " + System.IO.Path.GetFileName(d));
                }

                string[] files = System.IO.Directory.GetFiles(path);
                foreach (string f in files)
                {
                    entries.Add("[FILE] " + System.IO.Path.GetFileName(f));
                }

                AnsiConsole.MarkupLine("[gray][italic]done[/][/]");

                if (entries.Count == 0)
                {
                    return Task.FromResult("The directory is empty.");
                }
                return Task.FromResult("Contents of '" + path + "':" + "\n" + string.Join("\n", entries));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return Task.FromResult("Exploring directory failed. Exception message: " + ex.Message);
            }
        }
    }
}

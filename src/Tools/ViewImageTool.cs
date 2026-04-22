using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class ViewImageTool : ExecutableFunction
    {
        public ViewImageTool()
        {
            Name = "view_image";
            Description = "View an image that is either on the user's device or available at a URL.";
            InputParameters.Add(new FunctionInputParameter("path", "Either the path to the image file on the user's device OR the URL of where the image can be accessed online."));
        }

        public override Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string path = "";
            if (arguments != null)
            {
                JProperty? prop = arguments.Property("path");
                if (prop != null) path = prop.Value.ToString();
            }

            if (path == "")
            {
                return Task.FromResult("'path' parameter was empty!");
            }

            AnsiConsole.Markup("[gray][italic]checking image at '" + Markup.Escape(path) + "'... [/][/]");

            if (System.IO.File.Exists(path))
            {
                AnsiConsole.MarkupLine("[gray][italic]found[/][/]");
                return Task.FromResult("Image at '" + path + "' exists on the local file system. Note: direct image embedding in the conversation is not supported in this configuration. Describe what you need help with regarding this image, or use the read_file tool if it is a text-based format.");
            }
            else
            {
                AnsiConsole.MarkupLine("[gray][italic]noted[/][/]");
                return Task.FromResult("Image URL '" + path + "' has been noted. Note: direct image embedding in the conversation is not supported in this configuration. Describe what you need help with regarding this image.");
            }
        }
    }
}

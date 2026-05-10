using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Images;

namespace AIDA
{
    public class EditImageTool : ExecutableFunction
    {
        public EditImageTool()
        {
            Name = "edit_image";
            Description = "Generate an image from source images, using them as a reference.";
            InputParameters.Add(new FunctionInputParameter("input_paths", "List of input image paths, comma-separated."));
            InputParameters.Add(new FunctionInputParameter("description", "Description of the image to generate (edit to make)"));
            InputParameters.Add(new FunctionInputParameter("output_path", "File path to save output image to."));
        }

        public override async Task<string> ExecuteAsync(JObject? arguments = null)
        {
            if (arguments == null)
            {
                throw new Exception("Must provide arguments.");
            }

            //Validate settings
            AIDASettings settings = AIDASettings.Load();
            if (settings.ImageModel == null)
            {
                return "ImageModel not specified. Tell the user they must run /settings to provide connection info for a image model.";
            }

            //Get input file paths
            JProperty? prop_input_paths = arguments.Property("input_paths");
            if (prop_input_paths == null)
            {
                return "You must provide parameter 'input_paths'";
            }
            string input_paths_str = prop_input_paths.Value.ToString();
            string[] input_paths = input_paths_str.Split(new string[]{","}, StringSplitOptions.TrimEntries);

            //Get description
            JProperty? prop_description = arguments.Property("description");
            if (prop_description == null)
            {
                return "You must provide property 'description'.";
            }
            string description = prop_description.Value.ToString();

            //Get file path
            JProperty? prop_output_path = arguments.Property("output_path");
            if (prop_output_path == null)
            {
                return "You must provide property 'path'";
            }
            string output_path = prop_output_path.Value.ToString();

            //Setup
            FoundryResource fr = new FoundryResource(settings.ImageModel.FoundryUrl);

            //Is it auth via Service Principal? If so, check expiration
            if (settings.ImageModel.TenantID != null)
            {
                //check and update?
            }
            else //via API key
            {
                fr.ApiKey = settings.ImageModel.ApiKey;
            }

            //Create edit request
            ImageEditRequest ier = new ImageEditRequest();
            ier.Model = settings.ImageModel.ModelName;
            ier.Prompt = description;
            ier.Width = settings.ImageWidth;
            ier.Height = settings.ImageHeight;
            ier.Count = 1;
            ier.Quality =settings.ImageQuality;

            //Add each file
            foreach (string input_path in input_paths)
            {
                ier.AttachedImages.Add(new AttachedImage(input_path));
            }

            //Prompt!
            AnsiConsole.Markup("[gray][italic]editing... [/][/]");
            ImageGeneration ig;
            try
            {
                ig = await fr.EditImageAsync(ier);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[italic][red]error while generating[/][/]");
                return "Error while generating image: " + ex.Message;
            }

            //if not one
            if (ig.Images.Length != 1)
            {
                AnsiConsole.MarkupLine("[italic][red]image not present in array.[/][/]");
                return "Server returned but image was not present in array.";
            }

            //Save the image
            try
            {
                ig.Images[0].Save(output_path);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[italic][red]saving to path failed[/][/]");
                return "Image generated successfully but saving to '" + output_path + "' failed: " + ex.Message;
            }
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return "Image successfully saved to '" + output_path + "'.";
        }
    }
}
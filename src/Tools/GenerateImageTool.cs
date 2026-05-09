using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Images;

namespace AIDA
{
    public class GenerateImageTool : ExecutableFunction
    {
        public GenerateImageTool()
        {
            Name = "generate_image";
            Description = "Generate an image according to a description.";
            InputParameters.Add(new FunctionInputParameter("description", "Description of the image."));
            InputParameters.Add(new FunctionInputParameter("path", "File path to save to."));
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

            //Get properties
            JProperty? prop_description = arguments.Property("description");
            if (prop_description == null)
            {
                return "You must provide property 'description'.";
            }
            string description = prop_description.Value.ToString();

            //Get file path
            JProperty? prop_path = arguments.Property("path");
            if (prop_path == null)
            {
                return "You must provide property 'path'";
            }
            string path = prop_path.Value.ToString();

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

            //Create request
            ImageGenerationRequest igr = new ImageGenerationRequest();
            igr.Model = settings.ImageModel.ModelName;
            igr.Prompt = description;
            igr.Width = settings.ImageWidth;
            igr.Height = settings.ImageHeight;
            igr.Count = 1;
            igr.Quality = settings.ImageQuality;

            //Prompt!
            ImageGeneration ig;
            try
            {
                ig = await fr.GenerateImageAsync(igr);
            }
            catch (Exception ex)
            {
                return "Error while generating image: " + ex.Message;
            }

            //if not one
            if (ig.Images.Length == 1)
            {
                return "Server returned but image was not present in array.";
            }

            //Save the image
            try
            {
                ig.Images[0].Save(path);
            }
            catch (Exception ex)
            {
                return "Image generated successfully but saving to '" + path + "' failed: " + ex.Message;
            }

            return "Image successfully saved to '" + path + "'.";
        }
    }
}
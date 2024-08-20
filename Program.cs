using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Drawing;
using Microsoft.Extensions.Configuration;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
// Import namespaces
using Azure.AI.Vision.ImageAnalysis;
using Azure.Storage.Blobs;
using System.Collections.Generic;
using Azure.Storage.Blobs.Models;
using System.Linq;
using System.Drawing.Imaging;

// Import namespaces

namespace image_analysis
{
    class Program
    {
        
        private static SecretClient keyVaultClient;
        private static BlobServiceClient _blobServiceClient;
        private static BlobContainerClient _containerClient;
        static async Task Main(string[] args)
        {

            try
            {
                // Get config settings from AppSettings
                IConfigurationBuilder builder = new ConfigurationBuilder()
                    .AddUserSecrets<Program>();
                   
                IConfigurationRoot config = builder.Build();


                string? appTenant = config["appTenant"];
                string? appId = config["appId"] ?? null;
                string? appPassword = config["appPassword"] ?? null;
                string? keyVaultName = config["KeyVault"] ?? null;
                string indexName = config["SEARCH_INDEX_NAME"];
                string searchServiceAdminKey = config["SEARCH_ADMIN_KEY"];
                string searchServiceName = config["SEARCH_SERVICE_NAME"];

                var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
                ClientSecretCredential credential = new ClientSecretCredential(appTenant, appId, appPassword);
                keyVaultClient = new SecretClient(keyVaultUri, credential);

                 _blobServiceClient = new BlobServiceClient(keyVaultClient.GetSecret("StorageConnectionString").Value.Value);
             _containerClient = _blobServiceClient.GetBlobContainerClient("image-analysis");

                string? aiSvcEndpoint = config["AIServicesEndpoint"];
                string? aiSvcKey = config["AIServicesKey"];

                // Authenticate Azure AI Vision client
                ImageAnalysisClient client = new ImageAnalysisClient(
                    new Uri(aiSvcEndpoint),
                    new AzureKeyCredential(aiSvcKey));

                var imageFiles =  await ProcessImageFolder("raw-images");

                // Get image
                imageFiles.ForEach((_imageFile) =>
                {
                    // Analyze image
                    AnalyzeImage(_imageFile, client);

                    // Remove the background or generate a foreground matte from the image
                    // await BackgroundForeground(_imageFile, aiSvcEndpoint, aiSvcKey);
                });

                //var _imageFile = imageFiles.FirstOrDefault();

                //AnalyzeImage("raw-images/street.jpg", client);



            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static  void AnalyzeImage(string imageFile, ImageAnalysisClient client)
        {
            var memoryStream = new MemoryStream();
            BlobClient blobClient;
            try
            {
                Console.WriteLine($"\nAnalyzing {imageFile} \n");

                // Use a file stream to pass the image data to the analyze call
                blobClient = _containerClient.GetBlobClient(imageFile);
                
                blobClient.DownloadTo(memoryStream);
                memoryStream.Position = 0;
                var contentType = blobClient.GetProperties().Value.ContentType;
                // Get result with specified features to be retrieved
                ImageAnalysisResult result = client.Analyze(
                    BinaryData.FromStream(memoryStream),
                    VisualFeatures.Caption |
                    VisualFeatures.DenseCaptions |
                    VisualFeatures.Objects |
                    VisualFeatures.Tags |
                    VisualFeatures.People);


                // Display analysis results
                // Get image captions
                if (result.Caption.Text != null)
                {
                    Console.WriteLine(" Caption:");
                    Console.WriteLine($"   \"{result.Caption.Text}\", Confidence {result.Caption.Confidence:0.00}\n");
                }

                // Get image dense captions
                Console.WriteLine(" Dense Captions:");
                foreach (DenseCaption denseCaption in result.DenseCaptions.Values)
                {
                    Console.WriteLine($"   Caption: '{denseCaption.Text}', Confidence: {denseCaption.Confidence:0.00}");
                }

                // Get image tags
                if (result.Tags.Values.Count > 0)
                {
                    Console.WriteLine($"\n Tags:");
                    foreach (DetectedTag tag in result.Tags.Values)
                    {
                        Console.WriteLine($"   '{tag.Name}', Confidence: {tag.Confidence:F2}");
                    }
                }


                // Get objects in the image
                if (result.Objects.Values.Count > 0)
                {
                    Console.WriteLine(" Objects:");

                    // Prepare image for drawing
                    
                    System.Drawing.Image image = System.Drawing.Image.FromStream(memoryStream);
                    Graphics graphics = Graphics.FromImage(image);
                    Pen pen = new Pen(Color.Cyan, 3);
                    Font font = new Font("Arial", 16);
                    SolidBrush brush = new SolidBrush(Color.WhiteSmoke);

                    foreach (DetectedObject detectedObject in result.Objects.Values)
                    {
                        Console.WriteLine($"   \"{detectedObject.Tags[0].Name}\"");

                        // Draw object bounding box
                        var r = detectedObject.BoundingBox;
                        Rectangle rect = new Rectangle(r.X, r.Y, r.Width, r.Height);
                        graphics.DrawRectangle(pen, rect);
                        graphics.DrawString(detectedObject.Tags[0].Name, font, brush, (float)r.X, (float)r.Y);
                    }

                    
                    // Save annotated image
                    String output_file = $"processed/{DateTime.Now.ToString("yyyMMdd")}/objects.jpg";
                    blobClient = _containerClient.GetBlobClient(output_file);
                    using (MemoryStream imageMemStream = new MemoryStream())
                    {
                        image.Save(imageMemStream, ImageFormat.Jpeg);
                        imageMemStream.Position = 0;
                        blobClient.Upload(imageMemStream, true);
                    }
                    
                    
                    Console.WriteLine("  Results saved in " + output_file + "\n");
                }


                // Get people in the image
                if (result.People.Values.Count > 0)
                {
                    Console.WriteLine($" People:");

                    // Prepare image for drawing
                    System.Drawing.Image image = System.Drawing.Image.FromStream(memoryStream);
                    Graphics graphics = Graphics.FromImage(image);
                    Pen pen = new Pen(Color.Cyan, 3);
                    Font font = new Font("Arial", 16);
                    SolidBrush brush = new SolidBrush(Color.WhiteSmoke);

                    foreach (DetectedPerson person in result.People.Values)
                    {
                        // Draw object bounding box
                        var r = person.BoundingBox;
                        Rectangle rect = new Rectangle(r.X, r.Y, r.Width, r.Height);
                        graphics.DrawRectangle(pen, rect);

                        // Return the confidence of the person detected
                        //Console.WriteLine($"   Bounding box {person.BoundingBox.ToString()}, Confidence: {person.Confidence:F2}");
                    }

                    
                    // Save annotated image
                    String output_file = $"processed/{DateTime.Now.ToString("yyyMMdd")}/persons.jpg";
                    blobClient = _containerClient.GetBlobClient(output_file);
                    using (MemoryStream imageMemStream = new MemoryStream())
                    {
                        image.Save(imageMemStream, ImageFormat.Jpeg);
                        imageMemStream.Position = 0;
                        blobClient.Upload(imageMemStream, true);
                    }


                    //String output_file = "persons.jpg";
                    //image.Save(output_file);
                    Console.WriteLine("  Results saved in " + output_file + "\n");
                }
            }
            catch (Exception ex)
            {

                throw;
            }
            finally
            {
                memoryStream.Close();
                blobClient = null;
            }
            


        }
        static async Task BackgroundForeground(string imageFile, string endpoint, string key)
        {
            // Remove the background from the image or generate a foreground matte
            Console.WriteLine($" Background removal:");
            // Define the API version and mode
            string apiVersion = "2023-02-01-preview";
            string mode = "backgroundRemoval"; // Can be "foregroundMatting" or "backgroundRemoval"

            string url = $"computervision/imageanalysis:segment?api-version={apiVersion}&mode={mode}";

            // Make the REST call
            using (var client = new HttpClient())
            {
                var contentType = new MediaTypeWithQualityHeaderValue("application/json");
                client.BaseAddress = new Uri(endpoint);
                client.DefaultRequestHeaders.Accept.Add(contentType);
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", key);

                var data = new
                {
                    url = $"https://github.com/MicrosoftLearning/mslearn-ai-vision/blob/main/Labfiles/01-analyze-images/Python/image-analysis/{imageFile}?raw=true"
                };

                var jsonData = JsonSerializer.Serialize(data);
                var contentData = new StringContent(jsonData, Encoding.UTF8, contentType);
                var response = await client.PostAsync(url, contentData);

                if (response.IsSuccessStatusCode)
                {
                    File.WriteAllBytes("background.png", response.Content.ReadAsByteArrayAsync().Result);
                    Console.WriteLine("  Results saved in background.png\n");
                }
                else
                {
                    Console.WriteLine($"API error: {response.ReasonPhrase} - Check your body url, key, and endpoint.");
                }
            }

        }

        public async static Task<List<string>> ProcessImageFolder(string folderPath)
        {
            var blobs = new List<string>();
            List<string> _blobImageFiles= new();

        
            await foreach (var blobItem in _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, folderPath))
            {
                _blobImageFiles.Add(blobItem.Name);

            }

            return _blobImageFiles;
        }
    }
}

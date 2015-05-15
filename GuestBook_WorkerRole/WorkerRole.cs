using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Blob;
using GuestBook_Data;
using Microsoft.Azure;
using Microsoft.ProjectOxford.Face;

namespace GuestBook_WorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);
        private CloudQueue queue;
        private CloudBlobContainer container;
        private FaceServiceClient faceClient;

        public override void Run()
        {
            Trace.TraceInformation("GuestBook_WorkerRole is running");

            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        public override bool OnStart()
        {
            faceClient = new FaceServiceClient("61e4a07d7d62419b820f11e5c2fd75ac");
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;
            var storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("DataConnectionString"));

            CloudBlobClient blobStorage = storageAccount.CreateCloudBlobClient();
            this.container = blobStorage.GetContainerReference("guestbookpics");

            CloudQueueClient queueStorage = storageAccount.CreateCloudQueueClient();
            this.queue = queueStorage.GetQueueReference("guestthumbs");

            Trace.TraceInformation("Creating container and queue...");

            bool storageInitialized = false;
            while (!storageInitialized)
            {
                this.container.CreateIfNotExists();
                var permissions = this.container.GetPermissions();
                permissions.PublicAccess = BlobContainerPublicAccessType.Container;
                this.container.SetPermissions(permissions);

                this.queue.CreateIfNotExists();

                storageInitialized = true;
            }
            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            bool result = base.OnStart();

            Trace.TraceInformation("GuestBook_WorkerRole has been started");

            return result;
        }

        public override void OnStop()
        {
            Trace.TraceInformation("GuestBook_WorkerRole is stopping");

            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            base.OnStop();

            Trace.TraceInformation("GuestBook_WorkerRole has stopped");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following with your own logic.
            while (!cancellationToken.IsCancellationRequested)
            {
                Trace.TraceInformation("Working");
                CloudQueueMessage msg = this.queue.GetMessage();
                if (msg != null)
                {
                    var messageParts = msg.AsString.Split(new char[] { ',' });
                    var imageBlobName = messageParts[0];
                    var partitionKey = messageParts[1];
                    var rowkey = messageParts[2];

                    string thumbnailName = System.Text.RegularExpressions.Regex.Replace(imageBlobName, "([^\\.]+)(\\.[^\\.]+)?$", "$1-thumb$2");

                    CloudBlockBlob inputBlob = this.container.GetBlockBlobReference(imageBlobName);
                    CloudBlockBlob outputBlob = this.container.GetBlockBlobReference(thumbnailName);

                    using (Stream input = inputBlob.OpenRead())
                    using (Stream output = outputBlob.OpenWrite())
                    {
                        var faces = await faceClient.DetectAsync(input, false, true, true, false);
                        string faceMsg = string.Empty;
                        foreach (var face in faces)
                        {
                            faceMsg += face.Attributes.Gender + ":" + string.Format("{0:#} years old", face.Attributes.Age) + ",";
                        }
                        this.ProcessImage(input, output);

                        outputBlob.Properties.ContentType = "image/jpeg";
                        string thumbnailBlobUri = outputBlob.Uri.ToString();

                        GuestBookDataSource ds = new GuestBookDataSource();
                        ds.UpdateGuestBookEntry(partitionKey, rowkey, thumbnailBlobUri, faceMsg);

                        this.queue.DeleteMessage(msg);
                    }
                }
                await Task.Delay(1000);
            }
        }

        public void ProcessImage(Stream input, Stream output)
        {
            int width;
            int height;
            var originalImage = new Bitmap(input);

            if (originalImage.Width > originalImage.Height)
            {
                width = 128;
                height = 128 * originalImage.Height / originalImage.Width;
            }
            else
            {
                height = 128;
                width = 128 * originalImage.Width / originalImage.Height;
            }

            Bitmap thumbnailImage = null;

            try
            {
                thumbnailImage = new Bitmap(width, height);

                using (Graphics graphics = Graphics.FromImage(thumbnailImage))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(originalImage, 0, 0, width, height);
                }

                thumbnailImage.Save(output, ImageFormat.Jpeg);
            }
            finally
            {
                if (thumbnailImage != null)
                {
                    thumbnailImage.Dispose();
                }
            }
        }
    }
}

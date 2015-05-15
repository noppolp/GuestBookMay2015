using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GuestBook_Data
{
    public class GuestBookEntry
        : Microsoft.WindowsAzure.Storage.Table.TableEntity
    {
        public GuestBookEntry()
        {
            PartitionKey = DateTime.UtcNow.ToString("MMddyyyy");
            RowKey = string.Format("{0:10}_{1}",
                DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks,
                Guid.NewGuid());
        }

        public string Message { get; set; }
        public string GuestName { get; set; }
        public string PhotoUrl { get; set; }
        public string ThumbnailUrl { get; set; }
    }
}

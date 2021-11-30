using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace AMS_WebAPI.Areas.Identity.Data
{
    // Add profile data for application users by adding properties to the AMS_WebAPIUser class
    public class AMS_WebAPIUser : IdentityUser
    {
        public string DBName { get; set; }
    }
}

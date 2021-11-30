using API.Common.WebAPI;
using Microsoft.AspNetCore.Http;
using System;
using System.Security.Claims;

namespace AMS_WebAPI.Controllers
{
    public class ControllerUtility
    {
        static public Response GetDBNameFromIdentity(System.Security.Principal.IIdentity Identity, HttpRequest Request)
        {
            string DBName = "";

            try
            {
                var identity = Identity as ClaimsIdentity;
                if (identity != null)
                {
                    DBName = identity.FindFirst("DBName").Value;
                }
            }
            catch (Exception e)
            {
                return new Response(RESULT.GET_DBNAME_FROM_IDENTITY_ERROR, "", Request.Headers["Host"], e.Message);
            }

            if (string.IsNullOrEmpty(DBName))
            {
                return new Response(RESULT.NO_DBNAME_FOUND_IN_IDENTITY, "", Request.Headers["Host"], "No DBName found in Identity.");
            }

            return new Response(RESULT.SUCCESS, DBName, Request.Headers["Host"], DBName);
        }
    }
}

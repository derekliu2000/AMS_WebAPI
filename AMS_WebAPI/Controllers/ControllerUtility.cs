using API.Common.WebAPI;
using Microsoft.AspNetCore.Http;
using System;
using System.Security.Claims;

namespace AMS_WebAPI.Controllers
{
    public class ControllerUtility
    {
        static public IdentityResponse GetDBInfoFromIdentity(System.Security.Principal.IIdentity Identity, HttpRequest Request)
        {
            string DBName = "", DBUID = "", DBPassword = "";

            try
            {
                var identity = Identity as ClaimsIdentity;
                if (identity != null)
                {
                    DBName = identity.FindFirst("DBName").Value;
                    DBUID = identity.FindFirst("DBUID").Value;
                    DBPassword = identity.FindFirst("DBPassword").Value;
                }
            }
            catch (Exception e)
            {
                return new IdentityResponse(RESULT.GET_DBNAME_FROM_IDENTITY_ERROR, "", "", "", Request.Headers["Host"], e.Message);
            }

            if (string.IsNullOrEmpty(DBName))
            {
                return new IdentityResponse(RESULT.NO_DBNAME_FOUND_IN_IDENTITY, "", "", "", Request.Headers["Host"], "No DBName found in Identity.");
            }

            if (string.IsNullOrEmpty(DBUID))
            {
                return new IdentityResponse(RESULT.NO_DBUID_FOUND_IN_IDENTITY, DBName, "", "", Request.Headers["Host"], "No DBUID found in Identity.");
            }

            if (string.IsNullOrEmpty(DBPassword))
            {
                return new IdentityResponse(RESULT.NO_DBPASSWORD_FOUND_IN_IDENTITY, "", "", "", Request.Headers["Host"], "No DBPassword found in Identity.");
            }

            return new IdentityResponse(RESULT.SUCCESS, DBName, DBUID, DBPassword, Request.Headers["Host"], "");
        }

        static public string GetSiteDBConnString(string DBServer, IdentityResponse id)
        {
            string strDBConn = "Server={0};Database={1};UID={2};Password={3};MultipleActiveResultSets=true";
            return string.Format(strDBConn, DBServer, id.DB, id.DBUID, id.DBPassword);
        }
    }
}

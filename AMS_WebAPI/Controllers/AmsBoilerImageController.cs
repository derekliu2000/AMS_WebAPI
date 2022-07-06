using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.WebAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AmsBoilerImageController : ControllerBase
    {
        private readonly ILogger<AmsBarController> _logger;
        private readonly IConfiguration _configuration;

        public AmsBoilerImageController(ILogger<AmsBarController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // POST api/AmsBoilerImage
        [HttpPost]
        public async Task<IActionResult> PostAmsBoilerImage()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return UpdateBoilerImage(zippedData);
            }
        }

        private IActionResult UpdateBoilerImage(string zippedData)
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            Buffer_BoilerImg buffer;

            try
            {
                buffer = new Buffer_BoilerImg(zippedData, Request.Headers["Data-Hash"]);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.NEW_BUFFER, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            if (id.DB.ToUpper() != buffer.DBName.ToUpper())
            {
                Response rsp = new Response(RESULT.DBNAME_NOT_MATCH, buffer.DBName, Request.Headers["Host"], $"idDBName: {id.DB}; bufferDBName: {buffer.DBName}");
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            try
            {
                UpdateBoilerImage_SQL(buffer, id);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void UpdateBoilerImage_SQL(Buffer_BoilerImg boilerImageBuffer, IdentityResponse id)
        {
            try
            {
                string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand();
                    sqlCmd.Connection = sqlConn;

                    sqlCmd.CommandText = "IF EXISTS (SELECT * FROM AMS_BoilerImage WHERE ID=@ID) " +
                                         "UPDATE AMS_BoilerImage SET Image=@Image,LastUpdateUTC=@LastUpdateUTC WHERE ID=@ID " +
                                         "ELSE " +
                                         "INSERT INTO AMS_BoilerImage (ID,Image,LastUpdateUTC) VALUES (@ID,@Image,@LastUpdateUTC)";
                    sqlCmd.Parameters.AddWithValue("@ID", boilerImageBuffer.index + 1);
                    sqlCmd.Parameters.AddWithValue("@Image", boilerImageBuffer.binaryFile);
                    sqlCmd.Parameters.AddWithValue("@LastUpdateUTC", boilerImageBuffer.imgLastWriteUTC);
                    sqlCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }
    }
}

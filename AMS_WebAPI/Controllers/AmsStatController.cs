using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.WebAPI;
using Microsoft.AspNetCore.Authorization;
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
    [Authorize]
    public class AmsStatController : ControllerBase
    {
        private readonly ILogger<AmsStatController> _logger;
        private readonly IConfiguration _configuration;

        public AmsStatController(ILogger<AmsStatController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // POST api/<AmsStatController>
        [HttpPost]
        public async Task<IActionResult> PostAmsStat()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return UpdateAmsStat(zippedData);
            }
        }

        private IActionResult UpdateAmsStat(string zippedData)
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            Buffer_Stat buffer;

            try
            {
                buffer = new Buffer_Stat(zippedData, Request.Headers["Data-Hash"]);
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
                UpdateStat_SQL(buffer, id);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void UpdateStat_SQL(Buffer_Stat statBuffer, IdentityResponse id)
        {
            try
            {
                string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand();
                    sqlCmd.Connection = sqlConn;

                    for (int i = 0; i < statBuffer.chIdxList.Count; i++)
                    {
                        try
                        {
                            sqlCmd.Parameters.Clear();
                            sqlCmd.CommandText = "IF EXISTS (SELECT * FROM AMS_Stat WHERE ChannelIdx=@chIdx) " +
                                                 "UPDATE AMS_Stat SET AFBIdx=@AFBIdx,AFBJID=@AFBJID,CapRef=@CapRef,CapMeasured=@CapMeasured,AFBTemp=@AFBTemp WHERE ChannelIdx=@chIdx " +
                                                 "ELSE " +
                                                 "INSERT INTO AMS_Stat (AFBIdx,AFBJID,ChannelIdx,CapRef,CapMeasured,AFBTemp) VALUES (@AFBIdx,@AFBJID,@chIdx,@CapRef,@CapMeasured,@AFBTemp)";
                            sqlCmd.Parameters.AddWithValue("@AFBIdx", statBuffer.AFBIdxList[i]);
                            sqlCmd.Parameters.AddWithValue("@AFBJID", statBuffer.AFBJIDList[i]);
                            sqlCmd.Parameters.AddWithValue("@chIdx", statBuffer.chIdxList[i]);
                            sqlCmd.Parameters.AddWithValue("@CapRef", statBuffer.refList[i]);
                            sqlCmd.Parameters.AddWithValue("@CapMeasured", statBuffer.measList[i]);
                            sqlCmd.Parameters.AddWithValue("@AFBTemp", statBuffer.tempList[i]);
                            sqlCmd.ExecuteNonQuery();
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }
    }
}

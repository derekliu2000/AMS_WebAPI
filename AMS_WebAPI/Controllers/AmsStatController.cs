using API.Common.AMS;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
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
        public IActionResult PostAmsStat([FromBody] string value)
        {
            Buffer_Stat statBuffer;

            try
            {
                statBuffer = new Buffer_Stat(value, Request.Headers["Data-Hash"]);
                if (statBuffer.DBName == "")
                {
                    _logger.LogWarning($"new Buffer_SpecRef Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"new Buffer_Stat exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("new Buffer_Stat exception: " + e.Message);
            }

            try
            {
                UpdateStat_SQL(statBuffer);
            }
            catch (Exception e)
            {
                _logger.LogError($"UpdateStat_SQL exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("UpdateStat_SQL exception: " + e.Message);
            }

            return Ok("Success");
        }

        private void UpdateStat_SQL(Buffer_Stat statBuffer)
        {
            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), statBuffer.DBName);
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
    }
}

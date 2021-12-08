using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.DB;
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
    public class AmsBarController : ControllerBase
    {
        private readonly ILogger<AmsBarController> _logger;
        private readonly IConfiguration _configuration;

        public AmsBarController(ILogger<AmsBarController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // POST api/AmsBar
        [HttpPost]        
        public async Task<IActionResult> PostAmsBar()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return UpdateBar(zippedData);
            }
        }
        
        private IActionResult UpdateBar(string value)
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            Buffer_Bar buffer;

            try
            {
                buffer = new Buffer_Bar(value, Request.Headers["Data-Hash"]);
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
                UpdateBar_SQL(buffer, id);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void UpdateBar_SQL(Buffer_Bar barBuffer, IdentityResponse id)
        {
            SQLParts sqlParts = SQLParts.ConstructSQLParts(barBuffer.chEnList, barBuffer.auxEnList, "BAR", 0);

            string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = sqlConn.CreateCommand();
                SqlTransaction transaction = sqlConn.BeginTransaction("AddBarTrans");

                sqlCmd.Connection = sqlConn;
                sqlCmd.Transaction = transaction;

                try
                {
                    // Add values to AMS_DataBar table
                    sqlCmd.Parameters.Clear();
                    sqlCmd.CommandText = $"IF NOT EXISTS (SELECT * FROM AMS_DataBar WHERE UTC=@UTC) INSERT INTO AMS_DataBar ({sqlParts.ColumnNames}) VALUES ({sqlParts.Values})";
                    sqlCmd.Parameters.AddWithValue("@UTC", barBuffer.UTC);
                    for (int idx = 0; idx < barBuffer.chEnList.Count; idx++)
                        sqlCmd.Parameters.AddWithValue($"C{barBuffer.chEnList[idx] + 1}", barBuffer.chValList[idx]);
                    for (int idx = 0; idx < barBuffer.auxEnList.Count; idx++)
                        sqlCmd.Parameters.AddWithValue($"A{barBuffer.auxEnList[idx] + 1}", barBuffer.auxValList[idx]);
                    sqlCmd.ExecuteNonQuery();

                    // Add values to AMS_DataBarFlags_n tables
                    for (int tblIndex = 0; tblIndex < AMSDB.TABLE_BAR_FLAGS.Length; tblIndex++)
                    {
                        int minChId = AMSSysInfo.CH_COUNT / AMSDB.TABLE_BAR_FLAGS.Length * tblIndex;
                        int maxChId = AMSSysInfo.CH_COUNT / AMSDB.TABLE_BAR_FLAGS.Length * (tblIndex + 1) - 1;

                        sqlParts = SQLParts.ConstructSQLParts(barBuffer.chEnList, null, "BAR_FLAGS", tblIndex);
                        if (sqlParts.ContainValues)
                        {
                            sqlCmd.Parameters.Clear();
                            sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM {AMSDB.TABLE_BAR_FLAGS[tblIndex]} WHERE UTC=@UTC) " +
                                                 $"INSERT INTO {AMSDB.TABLE_BAR_FLAGS[tblIndex]} ({sqlParts.ColumnNames}) VALUES ({sqlParts.Values})";
                            for (int idx = 0; idx < barBuffer.chEnList.Count; idx++)
                            {
                                if (barBuffer.chEnList[idx] >= minChId && barBuffer.chEnList[idx] <= maxChId)
                                {
                                    sqlCmd.Parameters.AddWithValue($"@fC{barBuffer.chEnList[idx] + 1}", barBuffer.chMiscAlmPackList[idx]);
                                    sqlCmd.Parameters.AddWithValue($"@gC{barBuffer.chEnList[idx] + 1}", barBuffer.chGainList[idx]);
                                }
                            }
                            if (sqlCmd.Parameters.Count > 0)
                            {
                                sqlCmd.Parameters.AddWithValue("@UTC", barBuffer.UTC);
                                sqlCmd.ExecuteNonQuery();
                            }
                        }
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception ex2)
                    {
                        throw new Exception($"Failed to rollback. {ex2.Message} [{ex.SrcInfo()}]");
                    }

                    throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
                }
            }
        }
    }
}

using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.DB;
using API.Common.Utils;
using API.Common.WebAPI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AmsRMSController : ControllerBase
    {
        private readonly ILogger<AmsRMSController> _logger;
        private readonly IConfiguration _configuration;

        public AmsRMSController(ILogger<AmsRMSController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult GetRMSMaxUTC()
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            try
            {
                string DBName = Rijndael.Decrypt(Request.Headers["DBName"], Rijndael.EP_PASSPHRASE, Rijndael.EP_SALTVALUE, Rijndael.EP_HASHALGORITHM, Rijndael.EP_PASSWORDITERATIONS, Rijndael.EP_INITVECTOR, Rijndael.EP_KEYSIZE);
                if (id.DB.ToUpper() != DBName.ToUpper())
                {
                    Response rsp = new Response(RESULT.DBNAME_NOT_MATCH, DBName, Request.Headers["Host"], $"idDBName: {id.DB}; headDBName: {DBName}");
                    _logger.LogError(rsp.ToString());
                    return StatusCode(StatusCodes.Status500InternalServerError, rsp);
                }
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.DECRYPT_DB_NAME, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            try
            {
                DateTime maxUTC = GetRMSMaxUTC_SQL(Convert.ToInt32(Request.Headers["Param"]), id);
                return Ok(maxUTC);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }
        }

        private DateTime GetRMSMaxUTC_SQL(int tblIdx, IdentityResponse id)
        {
            try
            {
                DateTime maxUTC = DateTime.MinValue;

                string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand($"SELECT MAX(UTC) FROM {AMSDB.TABLE_RMS[tblIdx]}", sqlConn);
                    SqlDataReader dr = sqlCmd.ExecuteReader();
                    if (dr.HasRows)
                    {
                        while (dr.Read())
                        {
                            if (dr[0] != DBNull.Value)
                                maxUTC = dr.GetDateTime(0);
                        }
                    }
                    dr.Close();
                }

                return maxUTC;
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }

        // POST api/AmsBar
        [HttpPost("AddRMSBlocks")]
        public async Task<IActionResult> AddRMSBlocks()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return UpdateRMSBlocks(zippedData);
            }
        }
        
        private IActionResult UpdateRMSBlocks(string zippedData)
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            Buffer_RMS buffer;

            try
            {
                buffer = new Buffer_RMS(zippedData, Request.Headers["Data-Hash"]);
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
                for (int i = 0; i < buffer.blockCount; i++)
                {
                    DateTime UTC = DateTime.MinValue;
                    List<short> chValList = null;
                    List<short> auxValList = null;

                    Buffer_RMS.GetRMSRecordByIndex(buffer.rmsBuffer, i, buffer.chEnList, buffer.auxEnList, ref UTC, ref chValList, ref auxValList);
                    if (UTC != DateTime.MinValue)
                    {
                        AddOneRMSBlock(buffer, UTC, chValList, auxValList, id);
                    }
                }

                return Ok();
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }
        }

        private void AddOneRMSBlock(Buffer_RMS rmsBuffer, DateTime UTC, List<short> chValList, List<short> auxValList, IdentityResponse id)
        {
            SQLParts sqlParts;
            string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = sqlConn.CreateCommand();
                SqlTransaction transaction = sqlConn.BeginTransaction("AddRMSTrans");
                Boolean timestamp = false;

                sqlCmd.Connection = sqlConn;
                sqlCmd.Transaction = transaction;

                try
                {
                    for (int tblIndex = 0; tblIndex < AMSDB.TABLE_RMS.Length; tblIndex++)
                    {
                        int minChId = AMSSysInfo.CH_COUNT / AMSDB.TABLE_RMS.Length * tblIndex;
                        int maxChId = AMSSysInfo.CH_COUNT / AMSDB.TABLE_RMS.Length * (tblIndex + 1) - 1;

                        if (rmsBuffer.chEnList.Max() < minChId)
                            break;

                        sqlParts = SQLParts.ConstructSQLParts(rmsBuffer.chEnList, null, "RMS", tblIndex);
                        if (sqlParts.ContainValues)
                        {
                            // Add records to RMS tables
                            sqlCmd.Parameters.Clear();
                            sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM {AMSDB.TABLE_RMS[tblIndex]} WHERE UTC=@UTC) " +
                                                 $"INSERT INTO {AMSDB.TABLE_RMS[tblIndex]} ({sqlParts.ColumnNames}) VALUES ({sqlParts.Values})";
                            for (int idx = 0; idx < rmsBuffer.chEnList.Count; idx++)
                            {
                                if (rmsBuffer.chEnList[idx] >= minChId && rmsBuffer.chEnList[idx] <= maxChId)
                                {
                                    sqlCmd.Parameters.AddWithValue($"@C{rmsBuffer.chEnList[idx] + 1}", chValList[idx] & Convert.ToInt32("11111111111111", 2));
                                }
                            }
                            if (sqlCmd.Parameters.Count > 0)
                            {
                                sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                                sqlCmd.ExecuteNonQuery();
                            }

                            // Add records to RMSFlags tables
                            sqlCmd.Parameters.Clear();
                            sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM {AMSDB.TABLE_RMS_FLAGS[tblIndex]} WHERE UTC=@UTC) " +
                                                 $"INSERT INTO {AMSDB.TABLE_RMS_FLAGS[tblIndex]} ({sqlParts.ColumnNames}) VALUES ({sqlParts.Values})";
                            for (int idx = 0; idx < rmsBuffer.chEnList.Count; idx++)
                            {
                                if (rmsBuffer.chEnList[idx] >= minChId && rmsBuffer.chEnList[idx] <= maxChId)
                                {
                                    sqlCmd.Parameters.AddWithValue($"@C{rmsBuffer.chEnList[idx] + 1}", (chValList[idx] >> 14) & 3);
                                }
                            }
                            if (sqlCmd.Parameters.Count > 0)
                            {
                                sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                                sqlCmd.ExecuteNonQuery();

                                // Add timestamp to AMS_DataRMSTimestamp if needed
                                if (timestamp == false)
                                {
                                    sqlCmd.Parameters.Clear();
                                    sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM AMS_DataRMSTimestamp WHERE UTC=@UTC) " +
                                                         $"INSERT INTO AMS_DataRMSTimestamp (UTC) VALUES (@UTC)";
                                    sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                                    sqlCmd.ExecuteNonQuery();

                                    timestamp = true;
                                }
                            }
                        }
                    }

                    // Add aux record to AMS_DataAux table
                    sqlParts = SQLParts.ConstructSQLParts(rmsBuffer.auxEnList, null, "AUX", 0);
                    sqlCmd.Parameters.Clear();
                    sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM AMS_DataAux WHERE UTC=@UTC) " +
                                         $"INSERT INTO AMS_DataAux ({sqlParts.ColumnNames}) VALUES ({sqlParts.Values})";
                    sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                    for (int idx = 0; idx < auxValList.Count; idx++)
                    {
                        sqlCmd.Parameters.AddWithValue($"@A{rmsBuffer.auxEnList[idx] + 1}", auxValList[idx]);
                    }
                    sqlCmd.ExecuteNonQuery();

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
                        throw new Exception($"Roll back failed. {ex2.Message} [{ex.SrcInfo()}]");
                    }

                    throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
                }
            }
        }
    }
}

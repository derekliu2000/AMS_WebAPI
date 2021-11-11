using API.Common.AMS;
using API.Common.DB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AmsRMSController : ControllerBase
    {
        private readonly ILogger<AmsRMSController> _logger;
        private readonly IConfiguration _configuration;

        public AmsRMSController(ILogger<AmsRMSController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("RMSMaxUTC")]
        public IActionResult GetRMSMaxUTC(string p)
        {
            MaxUTCBuffer maxUTCBuffer;

            try
            {
                maxUTCBuffer = new MaxUTCBuffer(p, Request.Headers["Data-Hash"]);
                if (maxUTCBuffer.DBName == "")
                {
                    _logger.LogWarning($"new MaxUTCBuffer Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"new MaxUTCBuffer exception in GetRMSMaxUTC({Request.Headers["DBName"]}): " + e.Message);
                return BadRequest("new MaxUTCBuffer exception in GetRMSMaxUTC: " + e.Message);
            }

            try
            {
                DateTime maxUTC = GetRMSMaxUTC_SQL(maxUTCBuffer);
                return Ok(maxUTC);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Get RMS Max UTC from DB exception({Request.Headers["DBName"]}): " + ex.Message);
                return BadRequest("Get RMS Max UTC from DB exception: " + ex.Message);
            }
        }

        private DateTime GetRMSMaxUTC_SQL(MaxUTCBuffer maxUTCBuffer)
        {
            DateTime maxUTC = DateTime.MinValue;

            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), maxUTCBuffer.DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = new SqlCommand($"SELECT MAX(UTC) FROM {AMSDB.TABLE_RMS[maxUTCBuffer.tblIdx]}", sqlConn);
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

        [HttpPost("AddRMSBlocks")]
        public IActionResult AddRMSBlocks([FromBody] string value)
        {
            Buffer_RMS rmsBuffer;

            try
            {
                rmsBuffer = new Buffer_RMS(value, Request.Headers["Data-Hash"]);
                if (rmsBuffer.DBName == "")
                {
                    _logger.LogWarning($"new Buffer_RMS Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"new Buffer_RMS exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("new Buffer_RMS exception: " + e.Message);
            }

            try
            {
                for (int i = 0; i < rmsBuffer.blockCount; i++)
                {
                    DateTime UTC = DateTime.MinValue;
                    List<short> chValList = null;
                    List<short> auxValList = null;

                    Buffer_RMS.GetRMSRecordByIndex(rmsBuffer.rmsBuffer, i, rmsBuffer.chEnList, rmsBuffer.auxEnList, ref UTC, ref chValList, ref auxValList);
                    if (UTC != DateTime.MinValue)
                    {
                        AddOneRMSBlock(rmsBuffer, UTC, chValList, auxValList);
                    }
                }

                return Ok("Success");
            }
            catch (Exception ex)
            {
                _logger.LogError($"AddOneRMSBlock exception({Request.Headers["DBName"]}): " + ex.Message);
                return BadRequest(ex.Message);
            }
        }

        private void AddOneRMSBlock(Buffer_RMS rmsBuffer, DateTime UTC, List<short> chValList, List<short> auxValList)
        {
            SQLParts sqlParts;
            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), rmsBuffer.DBName);
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
                    catch
                    {

                    }
                }
            }
        }
    }
}

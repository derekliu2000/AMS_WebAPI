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
    public class AmsJournalController : ControllerBase
    {
        private readonly ILogger<AmsJournalController> _logger;
        private readonly IConfiguration _configuration;

        public AmsJournalController(ILogger<AmsJournalController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // GET api/AmsJournalController/JrnMaxUTC
        [HttpGet("JrnMaxUTC")]
        public IActionResult GetJrnMaxUTC(string p)
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
                _logger.LogError($"new MaxUTCBuffer exception({Request.Headers["DBName"]}): " + e.Message);
                return BadRequest("new MaxUTCBuffer exception in GetJrnMaxUTC: " + e.Message);
            }

            try
            {
                DateTime maxUTC = GetJrnMaxUTC_SQL(maxUTCBuffer);
                return Ok(maxUTC);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Get Jrn Max UTC from DB exception({Request.Headers["DBName"]}): " + ex.Message);
                return BadRequest("Get Jrn Max UTC from DB exception: " + ex.Message);
            }
        }

        private DateTime GetJrnMaxUTC_SQL(MaxUTCBuffer maxUTCBuffer)
        {
            DateTime maxUTC = DateTime.MinValue;

            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), maxUTCBuffer.DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = new SqlCommand($"SELECT MAX(UTC) FROM {AMSDB.TABLE_JRN[maxUTCBuffer.tblIdx]}", sqlConn);
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

        // POST api/AmsJournal
        [HttpPost]
        public IActionResult AmsJournal([FromBody] string value, string bufferHash)
        {
            Buffer_Journal journalBuffer;

            try
            {
                journalBuffer = new Buffer_Journal(value, Request.Headers["Data-Hash"]);
                if (journalBuffer.DBName == "")
                {
                    _logger.LogWarning($"new Buffer_Journal Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"new Buffer_Journal exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("new Buffer_Journal exception: " + e.Message);
            }

            try
            {
                for (int i = 0; i < journalBuffer.jrnList.Count; i++)
                {
                    JournalRecord jrn = journalBuffer.jrnList[i];
                    AddJournal_SQL(journalBuffer.DBName, jrn.UTC, journalBuffer.chIdxList, jrn.chValList);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"AddJournal_SQL exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("AddJournal_SQL exception: " + e.Message);
            }

            return Ok("Success");
        }

        private void AddJournal_SQL(string DBName, DateTime UTC, List<short> chIdList, List<short> chValList)
        {
            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = sqlConn.CreateCommand();
                SqlTransaction transaction = sqlConn.BeginTransaction("AddJrnTrans");
                Boolean timestamp = false;

                sqlCmd.Connection = sqlConn;
                sqlCmd.Transaction = transaction;

                try
                {
                    // Add values to AMS_DataBarFlags_n tables
                    for (int tblIndex = 0; tblIndex < AMSDB.TABLE_JRN.Length; tblIndex++)
                    {
                        int minChId = AMSSysInfo.CH_COUNT / AMSDB.TABLE_JRN.Length * tblIndex;
                        int maxChId = AMSSysInfo.CH_COUNT / AMSDB.TABLE_JRN.Length * (tblIndex + 1) - 1;

                        if (chIdList.Max() < minChId)
                            break;

                        SQLParts sqlParts = SQLParts.ConstructSQLParts(chIdList, null, "JRN", tblIndex);
                        if (sqlParts.ContainValues)
                        {
                            sqlCmd.Parameters.Clear();
                            sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM {AMSDB.TABLE_JRN[tblIndex]} WHERE UTC=@UTC) " +
                                                 $"INSERT INTO {AMSDB.TABLE_JRN[tblIndex]} ({sqlParts.ColumnNames}) VALUES ({sqlParts.Values})";
                            for (int idx = 0; idx < chIdList.Count; idx++)
                            {
                                if (chIdList[idx] >= minChId && chIdList[idx] <= maxChId)
                                {
                                    sqlCmd.Parameters.AddWithValue($"@C{chIdList[idx] + 1}", chValList[idx]);
                                }
                            }
                            if (sqlCmd.Parameters.Count > 0)
                            {
                                sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                                sqlCmd.ExecuteNonQuery();

                                // Add timestamp to AMS_JournalTimestamp if needed
                                if (timestamp == false)
                                {
                                    sqlCmd.Parameters.Clear();
                                    sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM AMS_JournalTimestamp WHERE UTC=@UTC) " +
                                                         $"INSERT INTO AMS_JournalTimestamp (UTC) VALUES (@UTC)";
                                    sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                                    sqlCmd.ExecuteNonQuery();

                                    timestamp = true;
                                }
                            }
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception ex2)
                    {
                        throw new Exception("Failed to rollback.", ex2);
                    }
                }
            }
        }
    }
}

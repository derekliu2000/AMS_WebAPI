using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.DB;
using API.Common.WebAPI;
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
        public IActionResult GetJrnMaxUTC()
        {
            try
            {
                DateTime maxUTC = GetJrnMaxUTC_SQL(Request.Headers["DBName"], Convert.ToInt32(Request.Headers["Param"]));
                return Ok(maxUTC);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }
        }

        private DateTime GetJrnMaxUTC_SQL(string DBName, int firstTblIdx)
        {
            try
            {
                DateTime maxUTC = DateTime.MinValue;

                string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), DBName);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand($"SELECT MAX(UTC) FROM {AMSDB.TABLE_JRN[firstTblIdx]}", sqlConn);
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

        // POST api/AmsJournal
        [HttpPost]
        public async Task<IActionResult> AmsJournal()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return AddJournal(zippedData);
            }
        }

        private IActionResult AddJournal(string zippedData)
        {
            Buffer_Journal journalBuffer;

            try
            {
                journalBuffer = new Buffer_Journal(zippedData, Request.Headers["Data-Hash"]);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.NEW_BUFFER, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
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
                Response rsp = new Response(RESULT.RUN_SQL, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
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

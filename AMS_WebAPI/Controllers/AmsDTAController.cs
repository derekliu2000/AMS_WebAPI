using API.Common.AMS;
using API.Common.DB;
using API.Common.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AmsDTAController : ControllerBase
    {
        private readonly ILogger<AmsDTAController> _logger;
        private readonly IConfiguration _configuration;

        public AmsDTAController(ILogger<AmsDTAController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("DTAMaxUTC")]
        public IActionResult GetDTAMaxUTC(string p)
        {
            MaxUTCBuffer maxUTCBuffer;

            try
            {
                maxUTCBuffer = new MaxUTCBuffer(p, Request.Headers["Data-Hash"], 0);
                if (maxUTCBuffer.DBName == "")
                {
                    _logger.LogWarning($"new MaxUTCBuffer Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"new MaxUTCBuffer exception({Request.Headers["DBName"]}): " + e.Message);
                return BadRequest("new MaxUTCBuffer exception in GetDTAMaxUTC: " + e.Message);
            }

            try
            {
                Dictionary<int, List<DateTime>> dic = GetDTAMaxUTC_SQL(maxUTCBuffer);
                byte[] buffer = Buffer_DTA.DicToArray(dic);
                string zippedString = ArrayOperate.ArrayToZippedString(buffer);

                string Json = JsonConvert.SerializeObject(dic);
                return Ok(zippedString);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Get DTA Max UTC from DB exception({Request.Headers["DBName"]}): " + ex.Message);
                return BadRequest("Get DTA Max UTC from DB exception: " + ex.Message);
            }
        }

        private Dictionary<int, List<DateTime>> GetDTAMaxUTC_SQL(MaxUTCBuffer maxUtCBuffer)
        {
            string strSqlCmd = "";
            Dictionary<int, List<DateTime>> dicDTAFileIdxToMaxUTC = new Dictionary<int, List<DateTime>>();

            for (int i = 0; i < maxUtCBuffer.DTAFileIdxList.Count; i++)
            {
                int fileIdx = maxUtCBuffer.DTAFileIdxList[i];
                int DTATblIdx = fileIdx / 12;
                strSqlCmd += (strSqlCmd.Length > 0 ? " UNION ALL " : "") +
                             $"SELECT {fileIdx},MAX(UTC) FROM {AMSDB.TABLE_DTA[DTATblIdx]} WHERE C{fileIdx * 2 + 1} IS NOT NULL " +
                             $"UNION ALL SELECT {fileIdx}, MAX(UTC) FROM {AMSDB.TABLE_DTA[DTATblIdx]} WHERE C{fileIdx * 2 + 2} IS NOT NULL";
            }

            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), maxUtCBuffer.DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = new SqlCommand();
                sqlCmd.Connection = sqlConn;

                sqlCmd.CommandText = strSqlCmd;
                SqlDataReader dr = sqlCmd.ExecuteReader();
                while (dr.Read())
                {
                    int fileIdx = dr.GetInt32(0);
                    if (dicDTAFileIdxToMaxUTC.ContainsKey(fileIdx) == false)
                    {
                        List<DateTime> dtList = new List<DateTime>();
                        dtList.Add(dr[1] == DBNull.Value ? DateTime.MinValue : dr.GetDateTime(1));
                        dicDTAFileIdxToMaxUTC.Add(fileIdx, dtList);
                    }
                    else
                    {
                        dicDTAFileIdxToMaxUTC[fileIdx].Add(dr[1] == DBNull.Value ? DateTime.MinValue : dr.GetDateTime(1));
                    }
                }
                dr.Close();
            }

            return dicDTAFileIdxToMaxUTC;
        }

        // POST api/<AmsDTAController>
        [HttpPost("WaveRecord")]
        public IActionResult AddWaveRecord([FromBody] string value)
        {
            Buffer_DTA DTABuffer;

            try
            {
                DTABuffer = new Buffer_DTA(value, Request.Headers["Data-Hash"]);
                if (DTABuffer.DBName == "")
                {
                    _logger.LogWarning($"new Buffer_DTA Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning($"new Buffer_DTA exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("new Buffer_DTA exception: " + e.Message);
            }

            try
            {
                for (int i = 0; i < DTABuffer.WDList.Count; i++)
                {
                    WaveData wd = DTABuffer.WDList[i];
                    AddWaveRecord_SQL(DTABuffer.DBName, wd.Idx, wd.flag, wd.UTC, wd.data);
                }

                return Ok("Success");
            }
            catch (Exception e)
            {
                _logger.LogWarning($"AddWaveRecord_SQL exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("AddWaveRecord_SQL exception: " + e.Message);
            }
        }

        private void AddWaveRecord_SQL(string DBName, int fileIdx, int flag, DateTime UTC, byte[][] data)
        {
            string where = "", update = "";
            if ((flag & 1) == 1)
            {
                where += $" AND C{fileIdx * 2 + 1} IS NOT NULL";
                update += $"C{fileIdx * 2 + 1}=@data1";
            }
            if ((flag & 2) == 2)
            {
                where += $" AND C{fileIdx * 2 + 2} IS NOT NULL";
                update += (update.Length == 0 ? "" : ",") + $"C{fileIdx * 2 + 2}=@data2";
            }

            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = new SqlCommand();
                sqlCmd.Connection = sqlConn;

                sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM {AMSDB.TABLE_DTA[fileIdx / 12]} WHERE UTC=@UTC) " +
                                     $"INSERT INTO {AMSDB.TABLE_DTA[fileIdx / 12]} (UTC) VALUES (@UTC)";
                sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                sqlCmd.ExecuteNonQuery();

                sqlCmd.Parameters.Clear();
                sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM {AMSDB.TABLE_DTA[fileIdx / 12]} WHERE UTC=@UTC {where}) " +
                                     $"UPDATE {AMSDB.TABLE_DTA[fileIdx / 12]} SET {update} WHERE UTC=@UTC";
                sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                if ((flag & 1) == 1)
                {
                    byte[] data1 = ArrayOperate.ZipData(data[0]);
                    if (data1.Length >= 512)
                        sqlCmd.Parameters.Add("@data1", SqlDbType.VarBinary, data[0].Length).Value = data[0];
                    else
                        sqlCmd.Parameters.Add("@data1", SqlDbType.VarBinary, data1.Length).Value = data1;
                }
                if ((flag & 2) == 2)
                {
                    byte[] data2 = ArrayOperate.ZipData(data[1]);
                    if (data2.Length >= 512)
                        sqlCmd.Parameters.Add("@data2", SqlDbType.VarBinary, data[1].Length).Value = data[1];
                    else
                        sqlCmd.Parameters.Add("@data2", SqlDbType.VarBinary, data2.Length).Value = data2;
                }
                sqlCmd.ExecuteNonQuery();

                // Add timestamp to AMS_DataDTATimestamp if needed
                sqlCmd.Parameters.Clear();
                sqlCmd.CommandText = $"IF NOT EXISTS (SELECT UTC FROM AMS_DataDTATimestamp WHERE UTC=@UTC) " +
                                     $"INSERT INTO AMS_DataDTATimestamp (UTC) VALUES (@UTC)";
                sqlCmd.Parameters.AddWithValue("@UTC", UTC);
                sqlCmd.ExecuteNonQuery();
            }
        }
    }
}

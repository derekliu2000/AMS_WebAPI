﻿using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.DB;
using API.Common.IO;
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
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
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
        public IActionResult GetDTAMaxUTC()
        {
            string[] strDTAFileIdxList;
            List<int> DTAFileIdxList;
            Dictionary<int, List<DateTime>> dic;

            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            try
            {
                strDTAFileIdxList = Request.Headers["Param"].ToString().Split(',');
                DTAFileIdxList = strDTAFileIdxList.Select(int.Parse).ToList();
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.GET_PARAM, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
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
                dic = GetDTAMaxUTC_SQL(DTAFileIdxList, id);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            try
            {
                byte[] buffer = Buffer_DTA.DicToArray(dic);
                string zippedString = ArrayOperate.ArrayToZippedString(buffer);
                return Ok(zippedString);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.SET_VALUE, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }
        }

        private Dictionary<int, List<DateTime>> GetDTAMaxUTC_SQL(List<int> DTAFileIdxList, IdentityResponse id)
        {
            try
            {
                string strSqlCmd = "";
                Dictionary<int, List<DateTime>> dicDTAFileIdxToMaxUTC = new Dictionary<int, List<DateTime>>();

                for (int i = 0; i < DTAFileIdxList.Count; i++)
                {
                    int fileIdx = DTAFileIdxList[i];
                    int DTATblIdx = fileIdx / 12;
                    strSqlCmd += (strSqlCmd.Length > 0 ? " UNION ALL " : "") +
                                 $"SELECT {fileIdx},MAX(UTC) FROM {AMSDB.TABLE_DTA[DTATblIdx]} WHERE C{fileIdx * 2 + 1} IS NOT NULL " +
                                 $"UNION ALL SELECT {fileIdx}, MAX(UTC) FROM {AMSDB.TABLE_DTA[DTATblIdx]} WHERE C{fileIdx * 2 + 2} IS NOT NULL";
                }

                string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
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
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }

        [HttpPost("WaveRecord")]
        public async Task<IActionResult> WaveRecord()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return AddWaveRecord(zippedData);
            }
        }        
        private IActionResult AddWaveRecord(string zippedData)
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            Buffer_DTA buffer;

            try
            {
                buffer = new Buffer_DTA(zippedData, Request.Headers["Data-Hash"]);
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
                for (int i = 0; i < buffer.WDList.Count; i++)
                {
                    WaveData wd = buffer.WDList[i];
                    AddWaveRecord_SQL(wd.Idx, wd.flag, wd.UTC, wd.data, id);
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

        private void AddWaveRecord_SQL(int fileIdx, int flag, DateTime UTC, byte[][] data, IdentityResponse id)
        {
            try
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

                string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
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
                        if (data1.Length > 600)
                            throw new Exception($"waveform data exceed limit. {data1.Length} > 600");
                        else
                            sqlCmd.Parameters.Add("@data1", SqlDbType.VarBinary, data1.Length).Value = data1;
                    }
                    if ((flag & 2) == 2)
                    {
                        byte[] data2 = ArrayOperate.ZipData(data[1]);
                        if (data2.Length > 600)
                            throw new Exception($"waveform data exceed limit. {data2.Length} > 600");
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
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }
    }
}

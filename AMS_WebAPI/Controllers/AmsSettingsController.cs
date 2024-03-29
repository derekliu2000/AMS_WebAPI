﻿using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.Utils;
using API.Common.WebAPI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
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
    public class AmsSettingsController : ControllerBase
    {
        private readonly ILogger<AmsSettingsController> _logger;
        private readonly IConfiguration _configuration;

        public AmsSettingsController(ILogger<AmsSettingsController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost]
        public async Task<IActionResult> PostAmsSetting()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return UpdateAmsSetting(zippedData);
            }
        }

        private IActionResult UpdateAmsSetting(string zippedData)
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            SettingBuffer buffer;

            try
            {
                buffer = new SettingBuffer(zippedData, Request.Headers["Data-Hash"]);
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
                UpdateSettings_SQL(buffer, id);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void UpdateSettings_SQL(SettingBuffer settingBuffer, IdentityResponse id)
        {
            try
            {
                byte[] byteLatestSettingsInDB = null;
                string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand();
                    sqlCmd.Connection = sqlConn;

                    // AMS_Settings table
                    DataSet ds = new DataSet();
                    sqlCmd.CommandText = "SELECT TOP 1 Settings FROM AMS_Settings ORDER BY LastUpdateUTC DESC,ID DESC";
                    SqlDataAdapter dataAdapter = new SqlDataAdapter(sqlCmd);
                    dataAdapter.Fill(ds);

                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0 && ds.Tables[0].Rows[0][0] != DBNull.Value)
                    {
                        byteLatestSettingsInDB = (byte[])ds.Tables[0].Rows[0][0];
                    }

                    if (byteLatestSettingsInDB == null ||
                        byteLatestSettingsInDB.SequenceEqual(settingBuffer.binaryZipped_t5set) == false)
                    {
                        sqlCmd.Parameters.Clear();
                        sqlCmd.CommandText = "INSERT INTO AMS_Settings (Settings,LastUpdateUTC) VALUES (@newSettings,@LastUpdateUTC)";
                        sqlCmd.Parameters.Add("@newSettings", SqlDbType.VarBinary, settingBuffer.binaryZipped_t5set.Length).Value = settingBuffer.binaryZipped_t5set;
                        sqlCmd.Parameters.AddWithValue("@LastUpdateUTC", settingBuffer.lastModifiedUTC);

                        sqlCmd.ExecuteNonQuery();
                    }

                    // AMS_BoilerWin_Binary table
                    ds = new DataSet();
                    sqlCmd.CommandText = "SELECT TOP 1 Settings FROM AMS_BoilerWin_Binary ORDER BY LastUpdateUTC DESC,ID DESC";
                    dataAdapter = new SqlDataAdapter(sqlCmd);
                    dataAdapter.Fill(ds);

                    byteLatestSettingsInDB = null;
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0 && ds.Tables[0].Rows[0][0] != DBNull.Value)
                    {
                        byteLatestSettingsInDB = (byte[])ds.Tables[0].Rows[0][0];
                    }

                    if (byteLatestSettingsInDB == null ||
                        byteLatestSettingsInDB.SequenceEqual(settingBuffer.binaryZippedOrgBinaryFile) == false)
                    {
                        sqlCmd.Parameters.Clear();
                        sqlCmd.CommandText = "INSERT INTO AMS_BoilerWin_Binary (Settings,LastUpdateUTC) VALUES (@newSettings,@LastUpdateUTC)";
                        sqlCmd.Parameters.Add("@newSettings", SqlDbType.VarBinary, settingBuffer.binaryZippedOrgBinaryFile.Length).Value = settingBuffer.binaryZippedOrgBinaryFile;
                        sqlCmd.Parameters.AddWithValue("@LastUpdateUTC", settingBuffer.lastModifiedUTC);

                        sqlCmd.ExecuteNonQuery();
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

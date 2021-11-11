using API.Common.AMS;
using API.Common.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Data;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AmsSettingsController : ControllerBase
    {
        private readonly ILogger<AmsSettingsController> _logger;
        private readonly IConfiguration _configuration;

        public AmsSettingsController(ILogger<AmsSettingsController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // POST: api/AmsSettings
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public IActionResult PostAmsSetting([FromBody] string zippedData)
        {
            SettingBuffer settingBuffer;

            try
            {
                settingBuffer = new SettingBuffer(zippedData, Request.Headers["Data-Hash"]);
                if (settingBuffer.DBName == "")
                {
                    _logger.LogWarning($"new SettingBuffer Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"new SettingBuffer exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("new SettingBuffer exception: " + e.Message);
            }

            try
            {
                UpdateSettings_SQL(settingBuffer);
            }
            catch (Exception e)
            {
                _logger.LogError($"UpdateSettings_SQL exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("UpdateSettings_SQL exception: " + e.Message);
            }

            return Ok("Success");
        }

        private void UpdateSettings_SQL(SettingBuffer settingBuffer)
        {
            string DBHashString = "";
            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), settingBuffer.DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = new SqlCommand();
                sqlCmd.Connection = sqlConn;

                DataSet ds = new DataSet();
                sqlCmd.CommandText = "SELECT TOP 1 Settings FROM AMS_Settings ORDER BY LastUpdateUTC DESC";
                SqlDataAdapter dataAdapter = new SqlDataAdapter(sqlCmd);
                dataAdapter.Fill(ds);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0 && ds.Tables[0].Rows[0][0] != DBNull.Value)
                {
                    byte[] data = (byte[])ds.Tables[0].Rows[0][0];
                    DBHashString = ArrayOperate.GetArrayHashString(data);
                }

                if (DBHashString != settingBuffer.checkSum)
                {
                    sqlCmd.Parameters.Clear();
                    sqlCmd.CommandText = "INSERT INTO AMS_Settings (Settings,LastUpdateUTC) VALUES (@newSettings,@LastUpdateUTC)";
                    sqlCmd.Parameters.Add("@newSettings", SqlDbType.VarBinary, settingBuffer.binaryZippedSettings.Length).Value = settingBuffer.binaryZippedSettings;
                    sqlCmd.Parameters.AddWithValue("@LastUpdateUTC", settingBuffer.lastModifiedUTC);

                    sqlCmd.ExecuteNonQuery();
                }
            }
        }
    }
}

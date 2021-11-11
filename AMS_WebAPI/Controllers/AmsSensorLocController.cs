using API.Common.AMS;
using API.Common.IO;
using API.Common.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Text;

namespace AMS_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AmsSensorLocController : ControllerBase
    {
        private readonly ILogger<AmsSensorLocController> _logger;
        private readonly IConfiguration _configuration;

        public AmsSensorLocController(ILogger<AmsSensorLocController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("GetSensorLocBinary")]
        public IActionResult GetSensorLocBinary(string p)
        {
            try
            {
                byte[] byteDBName = Convert.FromBase64String(CommonUtil.Base64UrlDecode(p));
                if (ArrayOperate.GetArrayHashString(byteDBName) == Request.Headers["Data-Hash"])
                {
                    string DBName = Encoding.ASCII.GetString(byteDBName);
                    return Ok(GetSensorLocBinary_SQL(DBName));
                }
                else
                {
                    _logger.LogWarning("Hash Error in GetSensorLocBinary()");
                    return Ok("Exception in GetSensorLocBinary(): Hash Error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception: GetSensorLocBinary()" + ex.Message);
                return Ok("Exception: GetSensorLocBinary(): " + ex.Message);
            }
        }

        private byte[] GetSensorLocBinary_SQL(string DBName)
        {
            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = new SqlCommand();
                sqlCmd.Connection = sqlConn;

                DataSet ds = new DataSet();
                sqlCmd.CommandText = "SELECT TOP 1 SensorLoc FROM AMS_SensorLocBinary ORDER BY UTC DESC";
                SqlDataAdapter dataAdapter = new SqlDataAdapter(sqlCmd);
                dataAdapter.Fill(ds);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0 && ds.Tables[0].Rows[0][0] != DBNull.Value)
                {
                    byte[] data = (byte[])ds.Tables[0].Rows[0][0];
                    return data;
                }

                return null;
            }
        }

        // POST api/AmsSensorLoc
        [HttpPost]
        public IActionResult AmsSensorLoc([FromBody] string value)
        {
            Buffer_SensorLoc sensorLocBuffer;

            try
            {
                sensorLocBuffer = new Buffer_SensorLoc(value, Request.Headers["Data-Hash"]);
                if (sensorLocBuffer.DBName == "")
                {
                    _logger.LogWarning($"new Buffer_SensorLoc Hash Error({Request.Headers["DBName"]})");
                    return Ok("Hash Error");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"new Buffer_Bar exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("new Buffer_Bar exception: " + e.Message);
            }

            try
            {
                AddSensorLoc_SQL(sensorLocBuffer);
            }
            catch (Exception e)
            {
                _logger.LogError($"AddSensorLoc_SQL exception({Request.Headers["DBName"]}): " + e.Message);
                return Ok("AddSensorLoc_SQL exception: " + e.Message);
            }

            return Ok("Success");
        }

        private void AddSensorLoc_SQL(Buffer_SensorLoc sensorLocBuffer)
        {
            string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), sensorLocBuffer.DBName);
            using (SqlConnection sqlConn = new SqlConnection(connectionString))
            {
                sqlConn.Open();

                SqlCommand sqlCmd = new SqlCommand();
                sqlCmd.Connection = sqlConn;

                sqlCmd.Parameters.Clear();
                sqlCmd.CommandText = "IF NOT EXISTS (SELECT * FROM AMS_SensorLocBinary WHERE UTC=@UTC) INSERT INTO AMS_SensorLocBinary (UTC, SensorLoc) VALUES (@UTC, @SensorLoc)";
                sqlCmd.Parameters.AddWithValue("@UTC", sensorLocBuffer.sensorLocLastWriteUTC);
                sqlCmd.Parameters.Add("@SensorLoc", SqlDbType.VarBinary, sensorLocBuffer.binaryCompressedFile.Length).Value = sensorLocBuffer.binaryCompressedFile;

                sqlCmd.ExecuteNonQuery();

                for (int i = 0; i < sensorLocBuffer.sensorChIdxList.Count; i++)
                {
                    sqlCmd.Parameters.Clear();
                    sqlCmd.CommandText = "IF NOT EXISTS (SELECT * FROM AMS_SensorLocation WHERE UTC=@UTC AND SensorID=@SensorID) " +
                                         "INSERT INTO AMS_SensorLocation (UTC, SensorID, xLoc, yLoc, Label, Type, IsInFront) " +
                                         "VALUES (@UTC, @SensorID, @xLoc, @yLoc, @Label, @Type, @IsInFront)";
                    sqlCmd.Parameters.AddWithValue("@UTC", sensorLocBuffer.sensorLocLastWriteUTC);
                    sqlCmd.Parameters.AddWithValue("@SensorID", sensorLocBuffer.sensorChIdxList[i] + 1);
                    sqlCmd.Parameters.AddWithValue("@xLoc", sensorLocBuffer.xLocList[i]);
                    sqlCmd.Parameters.AddWithValue("@yLoc", sensorLocBuffer.yLocList[i]);
                    sqlCmd.Parameters.AddWithValue("@Label", sensorLocBuffer.senTxtList[i]);
                    sqlCmd.Parameters.AddWithValue("@Type", sensorLocBuffer.senTypsList[i]);
                    sqlCmd.Parameters.AddWithValue("@IsInFront", sensorLocBuffer.blnFrontList[i]);

                    sqlCmd.ExecuteNonQuery();
                }
            }
        }
    }
}

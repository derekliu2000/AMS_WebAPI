using AMS_WebAPI.Models;
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
    public class AmsSensorLocController : ControllerBase
    {
        private readonly ILogger<AmsSensorLocController> _logger;
        private readonly IConfiguration _configuration;

        public AmsSensorLocController(ILogger<AmsSensorLocController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // POST api/AmsSensorLoc
        [HttpPost]
        public async Task<IActionResult> PostAmsBar()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return AddSensorLoc(zippedData);
            }
        }

        private IActionResult AddSensorLoc(string zippedData)
        {
            IdentityResponse id = ControllerUtility.GetDBInfoFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            Buffer_SensorLoc buffer;

            try
            {
                buffer = new Buffer_SensorLoc(zippedData, Request.Headers["Data-Hash"]);
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
                AddSensorLoc_SQL(buffer, id);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void AddSensorLoc_SQL(Buffer_SensorLoc sensorLocBuffer, IdentityResponse id)
        {
            try
            {
                byte[] byteSensorLoc = null;
                string connectionString = ControllerUtility.GetSiteDBConnString(_configuration.GetValue<string>("SiteDBServer"), id);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand();
                    sqlCmd.Connection = sqlConn;

                    // AMS_SensorLocBinary
                    DataSet ds = new DataSet();
                    sqlCmd.CommandText = "SELECT TOP 1 SensorLoc FROM AMS_SensorLocBinary ORDER BY UTC DESC";
                    SqlDataAdapter dataAdapter = new SqlDataAdapter(sqlCmd);
                    dataAdapter.Fill(ds);

                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0 && ds.Tables[0].Rows[0][0] != DBNull.Value)
                    {
                        byteSensorLoc = (byte[])ds.Tables[0].Rows[0][0];
                    }

                    if (byteSensorLoc == null ||
                        byteSensorLoc.SequenceEqual(sensorLocBuffer.binaryCompressedFile) == false)
                    {
                        sqlCmd.Parameters.Clear();
                        sqlCmd.CommandText = "INSERT INTO AMS_SensorLocBinary (UTC, SensorLoc) VALUES (@UTC, @SensorLoc)";
                        sqlCmd.Parameters.AddWithValue("@UTC", sensorLocBuffer.sensorLocLastWriteUTC);
                        sqlCmd.Parameters.Add("@SensorLoc", SqlDbType.VarBinary, sensorLocBuffer.binaryCompressedFile.Length).Value = sensorLocBuffer.binaryCompressedFile;
                        sqlCmd.ExecuteNonQuery();

                        // AMS_SensorLocation
                        sqlCmd.Parameters.Clear();
                        sqlCmd.CommandText = "DELETE AMS_SensorLocation WHERE UTC=@UTC";
                        sqlCmd.Parameters.AddWithValue("@UTC", sensorLocBuffer.sensorLocLastWriteUTC);
                        sqlCmd.ExecuteNonQuery();

                        for (int i = 0; i < sensorLocBuffer.sensorChIdxList.Count; i++)
                        {
                            sqlCmd.Parameters.Clear();
                            sqlCmd.CommandText = "INSERT INTO AMS_SensorLocation (UTC, SensorID, xLoc, yLoc, Label, Type, IsInFront) " +
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
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }
    }
}

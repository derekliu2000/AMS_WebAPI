﻿using AMS_WebAPI.Models;
using API.Common.AMS;
using API.Common.IO;
using API.Common.Utils;
using API.Common.WebAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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

        [HttpGet]
        public IActionResult GetSensorLocBinary()
        {
            try
            {
                return Ok(GetSensorLocBinary_SQL(Request.Headers["DBName"]));
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }
        }

        private byte[] GetSensorLocBinary_SQL(string DBName)
        {
            try
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
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
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
            Buffer_SensorLoc sensorLocBuffer;

            try
            {
                sensorLocBuffer = new Buffer_SensorLoc(zippedData, Request.Headers["Data-Hash"]);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.NEW_BUFFER, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            try
            {
                AddSensorLoc_SQL(sensorLocBuffer);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void AddSensorLoc_SQL(Buffer_SensorLoc sensorLocBuffer)
        {
            try
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
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }
    }
}

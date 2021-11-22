using AMS_WebAPI.Models;
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
    public class AmsSpecRefController : ControllerBase
    {
        private readonly ILogger<AmsSpecRefController> _logger;
        private readonly IConfiguration _configuration;

        public AmsSpecRefController(ILogger<AmsSpecRefController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // POST api/AmsSpecRef
        [HttpPost]
        public async Task<IActionResult> AmsSpecRef()
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string zippedData = await reader.ReadToEndAsync();
                return AddSpecRef(zippedData);
            }
        }
                
        private IActionResult AddSpecRef(string zippedData)
        {
            Buffer_SpecRef specRefBuffer;

            try
            {
                specRefBuffer = new Buffer_SpecRef(zippedData, Request.Headers["Data-Hash"]);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.NEW_BUFFER, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            try
            {
                AddSpecRef_SQL(specRefBuffer);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, Request.Headers["DBName"], Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void AddSpecRef_SQL(Buffer_SpecRef specRefBuffer)
        {
            try
            {
                string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), specRefBuffer.DBName);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand();
                    sqlCmd.Connection = sqlConn;

                    // AMS_SpecRefBinary
                    sqlCmd.Parameters.Clear();
                    sqlCmd.CommandText = "IF NOT EXISTS (SELECT * FROM AMS_SpecRefBinary WHERE UTC=@UTC) INSERT INTO AMS_SpecRefBinary (UTC, SpecRef) VALUES (@UTC, @SpecRef)";
                    sqlCmd.Parameters.AddWithValue("@UTC", specRefBuffer.specRefLastWriteUTC);
                    sqlCmd.Parameters.Add("@SpecRef", SqlDbType.VarBinary, specRefBuffer.binaryCompressedFile.Length).Value = specRefBuffer.binaryCompressedFile;
                    sqlCmd.ExecuteNonQuery();

                    // AMS_SpecRef_Channel_Binary
                    sqlCmd.Parameters.Clear();
                    sqlCmd.CommandText = "DELETE AMS_SpecRef_Channel_Binary WHERE UTC=@UTC";
                    sqlCmd.Parameters.AddWithValue("@UTC", specRefBuffer.specRefLastWriteUTC);
                    sqlCmd.ExecuteNonQuery();

                    for (int i = 0; i < specRefBuffer.chEnList.Count; i++)
                    {
                        byte[] curBlock = new byte[512];
                        byte[] compressedBlock = null;
                        if (Convert.ToInt16(specRefBuffer.binaryFile[specRefBuffer.chEnList[i]]) != 0)
                        {
                            Buffer.BlockCopy(specRefBuffer.binaryFile, (specRefBuffer.chEnList[i] + 1) * 512, curBlock, 0, 512);
                            compressedBlock = ArrayOperate.ZipData(curBlock);
                        }

                        sqlCmd.Parameters.Clear();
                        sqlCmd.CommandText = "INSERT INTO AMS_SpecRef_Channel_Binary (UTC, C_ID, Header, SpecRef) " +
                                             string.Format("VALUES (@UTC, @C_ID, @Header, {0})", compressedBlock == null ? "NULL" : "@SpecRef");
                        sqlCmd.Parameters.AddWithValue("@UTC", specRefBuffer.specRefLastWriteUTC);
                        sqlCmd.Parameters.AddWithValue("@C_ID", specRefBuffer.chEnList[i] + 1);
                        sqlCmd.Parameters.AddWithValue("@Header", Convert.ToInt16(specRefBuffer.binaryFile[specRefBuffer.chEnList[i]]));
                        if (compressedBlock != null)
                        {
                            sqlCmd.Parameters.Add("@SpecRef", SqlDbType.VarBinary, compressedBlock.Length).Value = compressedBlock;
                        }

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

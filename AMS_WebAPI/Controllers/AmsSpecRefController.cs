using AMS_WebAPI.Models;
using API.Common.AMS;
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
            Response id = ControllerUtility.GetDBNameFromIdentity(HttpContext.User.Identity, Request);
            if (id.Status != RESULT.SUCCESS)
            {
                _logger.LogError(id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, id);
            }

            Buffer_SpecRef buffer;

            try
            {
                buffer = new Buffer_SpecRef(zippedData, Request.Headers["Data-Hash"]);
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
                AddSpecRef_SQL(buffer);
            }
            catch (Exception e)
            {
                Response rsp = new Response(RESULT.RUN_SQL, id.DB, Request.Headers["Host"], e.Message);
                _logger.LogError(rsp.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, rsp);
            }

            return Ok();
        }

        private void AddSpecRef_SQL(Buffer_SpecRef specRefBuffer)
        {
            try
            {
                byte[] byteSpecRef = null;
                string connectionString = string.Format(_configuration.GetValue<string>("ConnectionStrings:SiteConnection"), specRefBuffer.DBName);
                using (SqlConnection sqlConn = new SqlConnection(connectionString))
                {
                    sqlConn.Open();

                    SqlCommand sqlCmd = new SqlCommand();
                    sqlCmd.Connection = sqlConn;

                    // AMS_SpecRefBinary
                    DataSet ds = new DataSet();
                    sqlCmd.CommandText = "SELECT TOP 1 SpecRef FROM AMS_SpecRefBinary ORDER BY UTC DESC";
                    SqlDataAdapter dataAdapter = new SqlDataAdapter(sqlCmd);
                    dataAdapter.Fill(ds);

                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0 && ds.Tables[0].Rows[0][0] != DBNull.Value)
                    {
                        byteSpecRef = (byte[])ds.Tables[0].Rows[0][0];
                    }

                    if (byteSpecRef == null ||
                        byteSpecRef.SequenceEqual(specRefBuffer.binaryCompressedFile) == false)
                    {
                        sqlCmd.Parameters.Clear();
                        sqlCmd.CommandText = "INSERT INTO AMS_SpecRefBinary (UTC, SpecRef) VALUES (@UTC, @SpecRef)";
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
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message} [{ex.SrcInfo()}]");
            }
        }
    }
}

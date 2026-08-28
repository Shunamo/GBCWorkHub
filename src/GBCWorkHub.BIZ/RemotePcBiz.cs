using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;
using GBCWorkHub.DTO.Enums;
using PcConnectionStatus = GBCWorkHub.DTO.Enums.RemotePcStatus;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// 원격 PC 조회 / 상태 확인 / RDP 접속
    /// </summary>
    public class RemotePcBiz
    {
        private readonly RemotePcDac _dac = new RemotePcDac();

        public List<RemoteSiteDto> GetSiteList()
        {
            return _dac.GetSiteList();
        }

        public List<RemotePcDto> GetRemotePcListBySite(string siteCode)
        {
            return _dac.GetRemotePcListBySite(siteCode);
        }

        public RemoteConnectionResultDto ConnectRdp(RemotePcDto pc)
        {
            if (pc == null)
            {
                return new RemoteConnectionResultDto
                {
                    Success = false,
                    Message = "선택된 PC가 없습니다."
                };
            }

            return ConnectRdp(new RemoteConnectionRequestDto
            {
                IpAddress = pc.IpAddress,
                Port = pc.RdpPort > 0 ? pc.RdpPort : 3389,
                PromptForCredentials = true,
                FullScreen = false
            });
        }

        /// <summary>
        /// ICMP Ping + RDP 포트(기본 3389) 도달 여부 확인
        /// </summary>
        public RemoteConnectionResultDto CheckReachability(string ipAddress, int port = 3389, int timeoutMs = 3000)
        {
            var result = new RemoteConnectionResultDto
            {
                CheckedAt = DateTime.Now,
                Status = PcConnectionStatus.Unknown
            };

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                result.Success = false;
                result.Message = "IP 주소를 입력하세요.";
                return result;
            }

            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send(ipAddress.Trim(), timeoutMs);
                    if (reply == null || reply.Status != IPStatus.Success)
                    {
                        result.Success = false;
                        result.Status = PcConnectionStatus.Offline;
                        result.Message = string.Format(
                            "Ping 실패: {0}",
                            reply == null ? "응답 없음" : reply.Status.ToString());
                        return result;
                    }

                    result.RoundtripTimeMs = reply.RoundtripTime;
                }

                bool portOpen = IsTcpPortOpen(ipAddress.Trim(), port, timeoutMs);
                if (!portOpen)
                {
                    result.Success = false;
                    result.Status = PcConnectionStatus.Offline;
                    result.Message = string.Format(
                        "Ping 성공({0}ms) / RDP 포트 {1} 닫힘 또는 차단됨",
                        result.RoundtripTimeMs,
                        port);
                    return result;
                }

                result.Success = true;
                result.Status = PcConnectionStatus.Available;
                result.Message = string.Format(
                    "접속 가능 (Ping {0}ms, 포트 {1} Open)",
                    result.RoundtripTimeMs,
                    port);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Status = PcConnectionStatus.Unknown;
                result.Message = "상태 확인 실패: " + ex.Message;
                return result;
            }
        }

        public Task<RemoteConnectionResultDto> CheckReachabilityAsync(string ipAddress, int port = 3389, int timeoutMs = 3000)
        {
            return Task.Run(() => CheckReachability(ipAddress, port, timeoutMs));
        }

        /// <summary>
        /// mstsc로 원격 데스크톱 실행
        /// </summary>
        public RemoteConnectionResultDto ConnectRdp(RemoteConnectionRequestDto request)
        {
            var result = new RemoteConnectionResultDto
            {
                CheckedAt = DateTime.Now
            };

            if (request == null || string.IsNullOrWhiteSpace(request.IpAddress))
            {
                result.Success = false;
                result.Message = "접속 대상 IP가 없습니다.";
                return result;
            }

            string target = request.IpAddress.Trim();
            if (request.Port > 0 && request.Port != 3389)
                target = string.Format("{0}:{1}", target, request.Port);

            try
            {
                string rdpFilePath = null;
                string args;

                if (!request.PromptForCredentials
                    && !string.IsNullOrWhiteSpace(request.UserName)
                    && !string.IsNullOrWhiteSpace(request.Password))
                {
                    StoreCredentials(request);
                    rdpFilePath = CreateTempRdpFile(request, target);
                    args = string.Format("\"{0}\"", rdpFilePath);
                }
                else
                {
                    args = string.Format("/v:{0}", target);
                    if (request.FullScreen)
                        args += " /f";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "mstsc.exe",
                    Arguments = args,
                    UseShellExecute = true
                };

                Process.Start(psi);

                result.Success = true;
                result.Status = PcConnectionStatus.InUse;
                result.Message = string.Format("원격 데스크톱 실행: {0}", target);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Status = PcConnectionStatus.Unknown;
                result.Message = "RDP 실행 실패: " + ex.Message;
                return result;
            }
        }

        private static bool IsTcpPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var asyncResult = client.BeginConnect(host, port, null, null);
                    bool success = asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs));
                    if (!success)
                        return false;

                    client.EndConnect(asyncResult);
                    return client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void StoreCredentials(RemoteConnectionRequestDto request)
        {
            string user = string.IsNullOrWhiteSpace(request.Domain)
                ? request.UserName
                : string.Format("{0}\\{1}", request.Domain, request.UserName);

            string targetName = string.Format("TERMSRV/{0}", request.IpAddress.Trim());

            // 기존 자격 증명 제거 후 등록
            RunSilent("cmdkey.exe", string.Format("/delete:{0}", targetName));
            RunSilent(
                "cmdkey.exe",
                string.Format("/generic:{0} /user:{1} /pass:{2}", targetName, user, request.Password));
        }

        private static string CreateTempRdpFile(RemoteConnectionRequestDto request, string target)
        {
            var sb = new StringBuilder();
            sb.AppendLine("full address:s:" + target);
            sb.AppendLine("prompt for credentials:i:0");
            sb.AppendLine("authentication level:i:2");
            sb.AppendLine(request.FullScreen ? "screen mode id:i:2" : "screen mode id:i:1");
            sb.AppendLine("desktopwidth:i:1280");
            sb.AppendLine("desktopheight:i:800");
            sb.AppendLine("session bpp:i:32");
            sb.AppendLine("compression:i:1");
            sb.AppendLine("username:s:" + (string.IsNullOrWhiteSpace(request.Domain)
                ? request.UserName
                : string.Format("{0}\\{1}", request.Domain, request.UserName)));

            string path = Path.Combine(Path.GetTempPath(), "GBCWorkHub_" + Guid.NewGuid().ToString("N") + ".rdp");
            File.WriteAllText(path, sb.ToString(), Encoding.Unicode);
            return path;
        }

        private static void RunSilent(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                    process.WaitForExit(5000);
            }
        }
    }
}

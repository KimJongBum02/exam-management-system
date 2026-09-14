using ExamManager.Shared;
using NetworkLib;
using System;
using System.Linq;
using System.Threading;

namespace StudentUI.Service
{
    // 이 PC 에 설치된 프로그램 목록을 교수 PC 로 보낸다.
    //
    // 교수의 프로그램 선택창은 교수 PC 에 설치된 것만 알아서, 강의실 PC 에만 깔린 프로그램은
    // 실행 파일 이름을 직접 쳐야 했다. 로그인할 때 한 번 보내 교수 쪽에 쌓이게 한다.
    // 교수 PC 는 받은 목록을 저장해 두므로, 다음 시험에는 학생이 접속하기 전에도 고를 수 있다.
    public static class InstalledProgramReport
    {
        // 로그인 패킷을 보낸 뒤에 불러야 한다. 교수 PC 는 로그인 전에 온 패킷을 버린다.
        public static void Send()
        {
            // 바로가기를 읽는 WScript.Shell 은 STA 스레드에서 부르는 것이 안전하다.
            // 1초 남짓 걸려 화면을 붙잡지 않도록 따로 돌린다.
            var worker = new Thread(() =>
            {
                try
                {
                    // 같은 프로그램의 바로가기가 여러 개일 수 있어 실행 파일 기준으로 하나만 보낸다.
                    var programs = StartMenuPrograms.Read()
                        .Select(found => found.Program)
                        .DistinctBy(program => program.ExecutableName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    NetworkService.Instance.SendPacket(PacketType.InstalledProgramsReport,
                                                       InstalledProgramsPayload.Encode(programs));
                }
                catch (Exception ex)
                {
                    // 못 보내도 시험 진행에는 영향이 없다. 교수 선택창에 강의실 목록이 늘지 않을 뿐이다.
                    System.Diagnostics.Debug.WriteLine($"설치 프로그램 목록을 보내지 못했습니다: {ex.Message}");
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
        }
    }
}

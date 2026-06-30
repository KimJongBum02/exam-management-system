using ProfessorUI.Service;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class DashBoardCardViewModel : INotifyPropertyChanged
    {
        // 💡 실시간 계산 프로퍼티들
        public int TotalStudents => StudentStore.Instance.Students.Count;

        // 접속 학생: '미접속' 또는 '오프라인'이 아닌 모든 학생 수 (환경에 맞게 문자열 수정 가능)
        public int ConnectedStudents => StudentStore.Instance.Students.Count(s => s.Status != "미접속");

        // 진행 중인 학생: Status가 "진행"인 학생 수
        public int ProgressStudents => StudentStore.Instance.Students.Count(s => s.Status == "진행");

        // 부정행위 학생: Status가 "부정행위"인 학생 수
        public int CheatStudents => StudentStore.Instance.Students.Count(s => s.Status == "부정행위");

        public DashBoardCardViewModel()
        {
            // 1. 학생 목록 자체가 바뀔 때 (추가/삭제) 감시 등록
            StudentStore.Instance.Students.CollectionChanged += Students_CollectionChanged;

            // 2. 현재 이미 목록에 존재하는 학생들의 상태 변화 감시 등록
            foreach (var student in StudentStore.Instance.Students)
            {
                if (student is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged += Student_PropertyChanged;
                }
            }
        }

        // 학생 목록이 늘어나거나 줄어들 때 호출됨
        private void Students_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is INotifyPropertyChanged npc) npc.PropertyChanged += Student_PropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is INotifyPropertyChanged npc) npc.PropertyChanged -= Student_PropertyChanged;
                }
            }

            // 목록이 바뀌었으므로 숫자 전면 새로고침
            UpdateAllCounts();
        }

        // 특정 학생의 개인 정보(예: Status)가 바뀔 때 호출됨
        private void Student_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 학생의 상태(Status) 프로퍼티가 바뀌면 대시보드 숫자를 다시 셉니다.
            if (e.PropertyName == "Status")
            {
                UpdateAllCounts();
            }
        }

        // 화면단(XAML)에 숫자가 바뀐 것을 통보하는 메서드
        private void UpdateAllCounts()
        {
            OnPropertyChanged(nameof(TotalStudents));
            OnPropertyChanged(nameof(ConnectedStudents));
            OnPropertyChanged(nameof(ProgressStudents));
            OnPropertyChanged(nameof(CheatStudents));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProfessorUI.ViewModel
{
    // '시험 시작/종료' 메뉴를 눌렀을 때 전체 화면을 총괄할 쟁반 뷰모델입니다.
    public class ExaminationMainViewModel : INotifyPropertyChanged
    {
        // ⭐ 시험 시작 카드를 담당할 자식 뷰모델을 선언합니다!
        public ExamStartViewModel ExamStartVM { get; }
        public AnswerCollectViewModel AnswerCollectVM { get; }
        public ExamEndViewModel ExamEndVM { get; }

        public ExaminationMainViewModel()
        {
            // ⭐ 쟁반이 열릴 때 3개를 동시에 만들어 둡니다.
            ExamStartVM = new ExamStartViewModel();
            AnswerCollectVM = new AnswerCollectViewModel();
            ExamEndVM = new ExamEndViewModel();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
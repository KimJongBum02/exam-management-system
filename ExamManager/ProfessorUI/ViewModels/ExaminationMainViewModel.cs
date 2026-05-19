using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProfessorUI.ViewModels
{
    // '시험 시작/종료' 메뉴를 눌렀을 때 전체 화면을 총괄할 쟁반 뷰모델입니다.
    public class ExaminationMainViewModel : INotifyPropertyChanged
    {
        public ExaminationMainViewModel()
        {
            // 나중에 여기에 각 카드별 자식 뷰모델들을 생성해서 연결해 줄 예정입니다.
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

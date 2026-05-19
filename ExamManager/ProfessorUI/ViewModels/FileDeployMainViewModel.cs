using System;
using System.Collections.Generic;
using System.Text;

namespace ProfessorUI.ViewModels
{
    public class FileDeployMainViewModel
    {
        // 두 개의 자식 뷰모델을 쥐고 있습니다.
        public FileReadyViewModel ReadyVM { get; }
        public FileDistributeViewModel DistributeVM { get; }

        public FileDeployMainViewModel()
        {
            // 쟁반이 생성될 때, 자식들도 같이 생성해줍니다.
            ReadyVM = new FileReadyViewModel();
            DistributeVM = new FileDistributeViewModel();
        }
    }
}

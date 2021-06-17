using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuSolver
{
    public class WrongLengthGivensException : Exception
    {
        public WrongLengthGivensException()
        {
        }

        public WrongLengthGivensException(string message)
            : base(message)
        {
        }

        public WrongLengthGivensException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}

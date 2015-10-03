/* ESRBRating.cs
 * Purpose: Class for ESRB ratings (e.g. E10+, T, M)
 * 
 * Revision History:
 *      Drew Matheson, 2015.10.02: Created
 */ 

using System.ComponentModel.DataAnnotations;

namespace Veil.Models
{
    /// <summary>
    /// An ESRB rating for a game
    /// </summary>
    public class ESRBRating
    {
        /// <summary>
        /// The Id for the rating
        /// <example>
        ///     E10+, T, M
        /// </example>
        /// </summary>
        [Key]
        public string RatingId { get; set; }

        /// <summary>
        /// The longer description for the rating
        /// </summary>
        [Required]
        public string Description { get; set; }
    }
}
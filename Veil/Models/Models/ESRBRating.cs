/* ESRBRating.cs
 * Purpose: Class for ESRB ratings (e.g. E10+, T, M)
 * 
 * Revision History:
 *      Drew Matheson, 2015.10.02: Created
 */

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Veil.DataModels.Models
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
        [StringLength(maximumLength: 8, MinimumLength = 1)]
        public string RatingId { get; set; }

        /// <summary>
        /// The longer description for the rating
        /// <example>
        ///     Everyone 10+, Teen, Mature
        /// </example>
        /// </summary>
        [Required]
        [StringLength(maximumLength: 64, MinimumLength = 1)]
        public string Description { get; set; }

        /// <summary>
        /// The URL for the image representation of this rating
        /// </summary>
        [DataType(DataType.Url)]
        [MaxLength(2048)]
        public string ImageURL { get; set; }

        /// <summary>
        /// Collection navigation property for games with this rating
        /// </summary>
        public virtual ICollection<Game> Games { get; set; }
    }
}
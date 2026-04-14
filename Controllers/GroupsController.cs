using APM.StaffZen.API.Data;
using APM.StaffZen.API.Dtos;
using APM.StaffZen.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APM.StaffZen.API.Controllers
{
    [ApiController]
    [Route("api/groups")]
    public class GroupsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GroupsController> _logger;

        public GroupsController(ApplicationDbContext context, ILogger<GroupsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/groups
        [HttpGet]
        public async Task<IActionResult> GetGroups()
        {
            try
            {
                var groups = await _context.Groups
                    .Select(g => new GroupDto
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Description = g.Description,
                        MemberCount = _context.Employees.Count(e => e.GroupId == g.Id)
                    })
                    .OrderBy(g => g.Name)
                    .ToListAsync();

                return Ok(groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving groups");
                return StatusCode(500, new { message = "An error occurred while retrieving groups" });
            }
        }

        // GET: api/groups/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetGroup(int id)
        {
            try
            {
                var group = await _context.Groups.FindAsync(id);

                if (group == null)
                    return NotFound(new { message = "Group not found" });

                var groupDto = new GroupDto
                {
                    Id = group.Id,
                    Name = group.Name,
                    Description = group.Description,
                    MemberCount = await _context.Employees.CountAsync(e => e.GroupId == id)
                };

                return Ok(groupDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving group with ID {GroupId}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the group" });
            }
        }

        // POST: api/groups
        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                {
                    return BadRequest(new { message = "Group name is required" });
                }

                // Check if group name already exists
                if (await _context.Groups.AnyAsync(g => g.Name.ToLower() == dto.Name.ToLower()))
                {
                    return BadRequest(new { message = "A group with this name already exists" });
                }

                var group = new Group
                {
                    Name = dto.Name.Trim(),
                    Description = dto.Description?.Trim(),
                    CreatedDate = DateTime.UtcNow
                };

                _context.Groups.Add(group);
                await _context.SaveChangesAsync();

                var groupDto = new GroupDto
                {
                    Id = group.Id,
                    Name = group.Name,
                    Description = group.Description,
                    MemberCount = 0
                };

                return CreatedAtAction(nameof(GetGroup), new { id = group.Id }, groupDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating group");
                return StatusCode(500, new { message = "An error occurred while creating the group" });
            }
        }

        // PUT: api/groups/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateGroup(int id, [FromBody] UpdateGroupDto dto)
        {
            try
            {
                var group = await _context.Groups.FindAsync(id);

                if (group == null)
                    return NotFound(new { message = "Group not found" });

                if (string.IsNullOrWhiteSpace(dto.Name))
                {
                    return BadRequest(new { message = "Group name is required" });
                }

                // Check if new name conflicts with existing group
                if (await _context.Groups.AnyAsync(g => g.Name.ToLower() == dto.Name.ToLower() && g.Id != id))
                {
                    return BadRequest(new { message = "A group with this name already exists" });
                }

                group.Name = dto.Name.Trim();
                group.Description = dto.Description?.Trim();

                await _context.SaveChangesAsync();

                var groupDto = new GroupDto
                {
                    Id = group.Id,
                    Name = group.Name,
                    Description = group.Description,
                    MemberCount = await _context.Employees.CountAsync(e => e.GroupId == id)
                };

                return Ok(groupDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating group with ID {GroupId}", id);
                return StatusCode(500, new { message = "An error occurred while updating the group" });
            }
        }

        // DELETE: api/groups/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteGroup(int id)
        {
            try
            {
                var group = await _context.Groups.FindAsync(id);

                if (group == null)
                    return NotFound(new { message = "Group not found" });

                // Remove group assignment from all employees in this group
                var employees = await _context.Employees.Where(e => e.GroupId == id).ToListAsync();
                foreach (var employee in employees)
                {
                    employee.GroupId = null;
                }

                _context.Groups.Remove(group);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Group deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting group with ID {GroupId}", id);
                return StatusCode(500, new { message = "An error occurred while deleting the group" });
            }
        }
    }
}

using APT_Market.Data;
using APT_Market.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APT_Market.Controllers;

[Authorize]
public class PropertyController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public PropertyController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // Akcja GET: Lista dostępnych lokali
    public async Task<IActionResult> Index()
    {
        var properties = await _context.Property
            .Where(p => p.IsAvailable)
            .ToListAsync();
        return View(properties);
    }

    // Akcja GET: Szczegóły lokalu
    public async Task<IActionResult> Details(int id)
    {
        var property = await _context.Property
            .Include(p => p.User) // Załaduj dane użytkownika
            .Include(p => p.ServiceRequests) // Jeśli potrzebujesz danych serwisowych
            .FirstOrDefaultAsync(p => p.Id == id);

        if (property == null)
        {
            return NotFound();
        }

        var currentUserId = _userManager.GetUserId(User);
        ViewBag.IsOwner = property.UserId == currentUserId;

        return View(property);
    }
    
    // Akcja GET: Tworzenie ogłoszenia
    [Authorize(Roles = "Admin,Wynajmujący")]
    public IActionResult Create()
    {
        return View(new Property());
    }
    
    // Akcja GET: Widok wystawionych przez danego usera ogłoszeń
    [Authorize(Roles = "Admin,Wynajmujący")]
    public async Task<IActionResult> MyListings()
    {
        var userId = _userManager.GetUserId(User);
        var properties = await _context.Property
            .Where(p => p.UserId == userId)
            .ToListAsync();

        return View(properties);
    }

    // Akcja GET: Widok wynajętych przez najemce lokali
    [Authorize(Roles = "Najemca")]
    public async Task<IActionResult> TenantIndex()
    {
        var userId = _userManager.GetUserId(User);
        var rentalAgreements = await _context.RentalAgreement
            .Where(ra => ra.UserId == userId)
            .Include(ra => ra.PropertyId)
            .ToListAsync();

        return View(rentalAgreements);
    }


    // Akcja POST: Wynajem lokalu
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rent(int id)
    {
        var property = await _context.Property.FindAsync(id);
        if (property == null || !property.IsAvailable) return BadRequest("Lokal niedostępny.");

        var rentalAgreement = new RentalAgreement
        {
            PropertyId = property.Id,
            TenantId = int.Parse(_userManager.GetUserId(User)),
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            MonthlyRent = property.RentPrice
        };

        property.IsAvailable = false;
        _context.RentalAgreement.Add(rentalAgreement);
        await _context.SaveChangesAsync();

        return RedirectToAction("Index");
    }

    // Akcja POST: Dodanie zgłoszenia serwisowego
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateServiceRequest(int propertyId, string description)
    {
        var property = await _context.Property.FindAsync(propertyId);
        if (property == null) return NotFound();

        var request = new MaintenanceRequest
        {
            PropertyId = propertyId,
            Description = description
        };

        _context.MaintenanceRequest.Add(request);
        await _context.SaveChangesAsync();

        return RedirectToAction("Details", new { id = propertyId });
    }

    // Akcja POST: Aktualizacja statusu zgłoszenia
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRequestStatus(int requestId, string status)
    {
        var request = await _context.MaintenanceRequest.FindAsync(requestId);
        if (request == null) return NotFound();

        request.Status = status;
        await _context.SaveChangesAsync();

        return RedirectToAction("MyListings");
    }
    
    // Akcja POST: Tworzenie ogłoszenia
    [HttpPost]
    [Authorize(Roles = "Admin,Wynajmujący")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Property model)
    {
        if (ModelState.IsValid)
        {
            var userId = _userManager.GetUserId(User);
            model.UserId = userId;
            model.IsAvailable = true;

            _context.Property.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction("MyListings");
        }

        return View(model);
    }
    
    [HttpGet]
    [Authorize(Roles = "Admin,Wynajmujący")]
    public async Task<IActionResult> Edit(int id)
    {
        var property = await _context.Property.FirstOrDefaultAsync(p => p.Id == id);

        if (property == null)
        {
            return NotFound();
        }

        var dto = new PropertyUpdateDto
        {
            Id = property.Id,
            Description = property.Description,
            RentPrice = property.RentPrice
        };

        return View(dto);
    }
    
    [HttpPost]
    [Authorize(Roles = "Admin,Wynajmujący")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PropertyUpdateDto dto)
    {
        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var property = await _context.Property.FirstOrDefaultAsync(p => p.Id == dto.Id);

        if (property == null)
        {
            return NotFound();
        }

        // Mapowanie danych z DTO
        property.Description = dto.Description;
        property.RentPrice = dto.RentPrice;

        _context.Update(property);
        await _context.SaveChangesAsync();

        return RedirectToAction("MyListings");
    }


    [HttpPost]
    [Authorize(Roles = "Admin,Wynajmujący")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTenant(int id)
    {
        var property = await _context.Property
            .Include(p => p.ServiceRequests)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (property == null || property.IsAvailable) return NotFound();

        // Znajdź umowę najmu i usuń
        var rentalAgreement = await _context.RentalAgreement
            .FirstOrDefaultAsync(ra => ra.PropertyId == id);
        if (rentalAgreement != null)
        {
            _context.RentalAgreement.Remove(rentalAgreement);
        }

        // Usuń zgłoszenia serwisowe
        _context.MaintenanceRequest.RemoveRange(property.ServiceRequests);

        // Oznacz lokal jako dostępny
        property.IsAvailable = true;
        property.UserId = null;

        await _context.SaveChangesAsync();
        return RedirectToAction("Edit", new { id = property.Id });
    }

}
